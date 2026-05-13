using System.Text.Json;
using Engram.Store;
using Engram.Store.Cloud;

namespace Engram.Store.Tests;

/// <summary>
/// Test contracts for CloudAuditLog — derived from PRD Phase 8 requirements:
/// - Every cloud call records reason, provider, payload summary, and cost (SC-2)
/// - Cloud call -> audit log entry with reason + cost (Quality Gate)
/// - Append-only log
/// </summary>
public class CloudAuditLogTests : IDisposable
{
    private readonly string _tempDir;

    public CloudAuditLogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"engram_audit_{Guid.NewGuid():n}");
        Directory.CreateDirectory(_tempDir);
    }

    // --- Basic logging ---

    [Fact]
    public void Log_Creates_Audit_Entry()
    {
        using var log = new CloudAuditLog(_tempDir);

        var entry = CreateTestEntry();
        log.Log(entry);

        var entries = log.ReadAll();
        Assert.Single(entries);
    }

    [Fact]
    public void Entry_Has_All_Required_Fields()
    {
        using var log = new CloudAuditLog(_tempDir);

        var entry = CreateTestEntry();
        log.Log(entry);

        var read = log.ReadAll().Single();
        Assert.NotEmpty(read.EntryId);
        Assert.True(read.Timestamp > DateTimeOffset.MinValue);
        Assert.Equal("Test research query", read.Reason);
        Assert.Equal("gemini-flash", read.Provider);
        Assert.Equal("gemini-3-flash", read.Model);
        Assert.Equal("summarized payload", read.PayloadSummary);
        Assert.Equal(100, read.InputTokens);
        Assert.Equal(50, read.OutputTokens);
        Assert.Equal(0.001m, read.CostUsd);
        Assert.True(read.Success);
        Assert.False(read.FromCache);
        Assert.Equal("Medium", read.TaskComplexity);
        Assert.Equal("GeminiFlash", read.ComputeTarget);
    }

    // --- Append-only ---

    [Fact]
    public void Log_Is_Append_Only()
    {
        using var log = new CloudAuditLog(_tempDir);

        log.Log(CreateTestEntry(reason: "First call"));
        log.Log(CreateTestEntry(reason: "Second call"));
        log.Log(CreateTestEntry(reason: "Third call"));

        var entries = log.ReadAll();
        Assert.Equal(3, entries.Count);
        Assert.Equal("First call", entries[0].Reason);
        Assert.Equal("Second call", entries[1].Reason);
        Assert.Equal("Third call", entries[2].Reason);
    }

    [Fact]
    public void Log_Does_Not_Overwrite_Previous_Entries()
    {
        using var log = new CloudAuditLog(_tempDir);

        log.Log(CreateTestEntry(reason: "First"));
        log.Log(CreateTestEntry(reason: "Second"));

        // Re-read to verify both are present
        var entries = log.ReadAll();
        Assert.Equal(2, entries.Count);
    }

    // --- JSONL format ---

    [Fact]
    public void Log_File_Is_Valid_JSONL()
    {
        using var log = new CloudAuditLog(_tempDir);

        log.Log(CreateTestEntry());
        log.Log(CreateTestEntry());

        var logPath = Path.Combine(_tempDir, "cloud-audit.jsonl");
        Assert.True(File.Exists(logPath));

        var lines = File.ReadAllLines(logPath);
        Assert.Equal(2, lines.Length);

        // Each line is valid JSON
        foreach (var line in lines)
        {
            var entry = JsonSerializer.Deserialize<CloudAuditEntry>(line);
            Assert.NotNull(entry);
        }
    }

    // --- Cost tracking ---

    [Fact]
    public void GetTotalCost_Sums_All_Entries()
    {
        using var log = new CloudAuditLog(_tempDir);

        log.Log(CreateTestEntry(cost: 0.05m));
        log.Log(CreateTestEntry(cost: 0.10m));
        log.Log(CreateTestEntry(cost: 0.03m));

        Assert.Equal(0.18m, log.GetTotalCost());
    }

    [Fact]
    public void GetTotalCost_Returns_Zero_When_Empty()
    {
        using var log = new CloudAuditLog(_tempDir);

        Assert.Equal(0m, log.GetTotalCost());
    }

    // --- Date range filtering ---

    [Fact]
    public void GetEntriesInRange_Filters_By_Date()
    {
        using var log = new CloudAuditLog(_tempDir);

        var now = DateTimeOffset.UtcNow;
        log.Log(CreateTestEntry(timestamp: now.AddHours(-2)));
        log.Log(CreateTestEntry(timestamp: now.AddHours(-1)));
        log.Log(CreateTestEntry(timestamp: now));

        var recent = log.GetEntriesInRange(now.AddMinutes(-90), now.AddMinutes(1));
        Assert.Equal(2, recent.Count);
    }

    [Fact]
    public void GetEntriesInRange_Returns_Empty_When_No_Matches()
    {
        using var log = new CloudAuditLog(_tempDir);

        log.Log(CreateTestEntry(timestamp: DateTimeOffset.UtcNow.AddDays(-10)));

        var recent = log.GetEntriesInRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        Assert.Empty(recent);
    }

    // --- Success/failure tracking ---

    [Fact]
    public void Failed_Call_Is_Logged_With_Error()
    {
        using var log = new CloudAuditLog(_tempDir);

        log.Log(new CloudAuditEntry
        {
            Reason = "Failed call",
            Provider = "gemini-flash",
            Success = false,
            ErrorMessage = "Rate limit exceeded",
            CostUsd = 0
        });

        var entry = log.ReadAll().Single();
        Assert.False(entry.Success);
        Assert.Equal("Rate limit exceeded", entry.ErrorMessage);
    }

    // --- Empty log ---

    [Fact]
    public void ReadAll_Returns_Empty_When_No_Log_File()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), $"engram_empty_{Guid.NewGuid():n}");
        Directory.CreateDirectory(emptyDir);

        using var log = new CloudAuditLog(emptyDir);
        var entries = log.ReadAll();

        Assert.Empty(entries);
    }

    // --- Disposed ---

    [Fact]
    public void Log_After_Dispose_Throws()
    {
        var log = new CloudAuditLog(_tempDir);
        log.Dispose();

        Assert.Throws<ObjectDisposedException>(() => log.Log(CreateTestEntry()));
    }

    [Fact]
    public void ReadAll_After_Dispose_Throws()
    {
        var log = new CloudAuditLog(_tempDir);
        log.Dispose();

        Assert.Throws<ObjectDisposedException>(() => log.ReadAll());
    }

    // --- Constructor validation ---

    [Fact]
    public void Constructor_Null_Path_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CloudAuditLog(null!));
    }

    // --- Helpers ---

    private static CloudAuditEntry CreateTestEntry(
        string reason = "Test research query",
        decimal cost = 0.001m,
        DateTimeOffset? timestamp = null)
    {
        return new CloudAuditEntry
        {
            Reason = reason,
            Provider = "gemini-flash",
            Model = "gemini-3-flash",
            PayloadSummary = "summarized payload",
            InputTokens = 100,
            OutputTokens = 50,
            CostUsd = cost,
            Success = true,
            FromCache = false,
            TaskComplexity = "Medium",
            ComputeTarget = "GeminiFlash",
            Timestamp = timestamp ?? DateTimeOffset.UtcNow
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
