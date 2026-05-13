using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for deterministic content hashing.
/// Derived from: REQ-005, D-007, Implementation Plan §5
///
/// PRD Contract:
/// - "Compute deterministic content hashes and prevent duplicate raw events"
/// - "Hash calculation is deterministic across runs"
/// - "content-addressed hash for duplicate detection"
/// - "no destructive edits"
/// </summary>
public class ContentHasherTests
{
    private readonly ContentHasher _sut = new();

    [Fact]
    public void ComputeHash_ReturnsNonEmptyString()
    {
        var evt = TestEvents.Create();
        var hash = _sut.ComputeHash(evt);

        Assert.False(string.IsNullOrWhiteSpace(hash));
    }

    [Fact]
    public void ComputeHash_IsDeterministic_SameInputSameOutput()
    {
        // REQ-005, D-007: "Hash calculation is deterministic across runs"
        var evt = TestEvents.Create();
        evt.EventId = "fixed-id";
        evt.Text = "deterministic test content";
        evt.CapturedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var hash1 = _sut.ComputeHash(evt);
        var hash2 = _sut.ComputeHash(evt);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_IsDeterministic_AcrossMultipleCalls()
    {
        var evt = TestEvents.Create();

        var hashes = Enumerable.Range(0, 100)
            .Select(_ => _sut.ComputeHash(evt))
            .Distinct()
            .Count();

        Assert.Equal(1, hashes);
    }

    [Fact]
    public void ComputeHash_DifferentContent_ProducesDifferentHash()
    {
        var evt1 = TestEvents.Create(text: "content A");
        var evt2 = TestEvents.Create(text: "content B");

        var hash1 = _sut.ComputeHash(evt1);
        var hash2 = _sut.ComputeHash(evt2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentEventType_ProducesDifferentHash()
    {
        var evt1 = TestEvents.Create(eventType: "screen_capture");
        var evt2 = TestEvents.Create(eventType: "clipboard");

        Assert.NotEqual(_sut.ComputeHash(evt1), _sut.ComputeHash(evt2));
    }

    [Fact]
    public void ComputeHash_DifferentSource_ProducesDifferentHash()
    {
        var evt1 = TestEvents.Create(source: "ocr");
        var evt2 = TestEvents.Create(source: "file_watcher");

        Assert.NotEqual(_sut.ComputeHash(evt1), _sut.ComputeHash(evt2));
    }

    [Fact]
    public void ComputeHash_DifferentCapturedAt_ProducesDifferentHash()
    {
        var evt1 = TestEvents.Create(capturedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var evt2 = TestEvents.Create(capturedAt: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

        Assert.NotEqual(_sut.ComputeHash(evt1), _sut.ComputeHash(evt2));
    }

    [Fact]
    public void ComputeHash_IgnoresEventId()
    {
        // EventId is the filename, not part of content identity
        // Two events with same content but different IDs should hash the same
        var evt1 = TestEvents.Create(text: "same content");
        var evt2 = TestEvents.Create(text: "same content");
        evt1.EventId = "id-1";
        evt2.EventId = "id-2";
        evt1.CapturedAt = evt2.CapturedAt;
        evt1.EventType = evt2.EventType;
        evt1.Source = evt2.Source;

        Assert.Equal(_sut.ComputeHash(evt1), _sut.ComputeHash(evt2));
    }

    [Fact]
    public void ComputeHash_IgnoresHashFieldItself()
    {
        // The hash field on the event should not influence the computed hash
        var evt1 = TestEvents.Create();
        var evt2 = TestEvents.Create();
        evt1.CapturedAt = evt2.CapturedAt;
        evt1.EventType = evt2.EventType;
        evt1.Source = evt2.Source;
        evt1.Text = evt2.Text;
        evt1.Hash = "old-hash";
        evt2.Hash = "different-hash";

        Assert.Equal(_sut.ComputeHash(evt1), _sut.ComputeHash(evt2));
    }

    [Fact]
    public void ComputeHash_IgnoresProcessingStatus()
    {
        var evt1 = TestEvents.Create();
        var evt2 = TestEvents.Create();
        evt1.CapturedAt = evt2.CapturedAt;
        evt1.EventType = evt2.EventType;
        evt1.Source = evt2.Source;
        evt1.Text = evt2.Text;
        evt1.ProcessingStatus = "pending";
        evt2.ProcessingStatus = "processed";

        Assert.Equal(_sut.ComputeHash(evt1), _sut.ComputeHash(evt2));
    }

    [Fact]
    public void ComputeHash_ReturnsHexString()
    {
        var evt = TestEvents.Create();
        var hash = _sut.ComputeHash(evt);

        Assert.Matches("^[a-f0-9]+$", hash);
    }

    [Fact]
    public void ComputeHash_ProducesExpectedLength()
    {
        // SHA-256 produces 64 hex characters
        var evt = TestEvents.Create();
        var hash = _sut.ComputeHash(evt);

        Assert.Equal(64, hash.Length);
    }
}
