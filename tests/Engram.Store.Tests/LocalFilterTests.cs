using Engram.Store;
using Engram.Store.Cloud;

namespace Engram.Store.Tests;

/// <summary>
/// Test contracts for LocalFilter — derived from PRD Phase 8 requirements:
/// - Private raw data is never sent without explicit policy approval (SC-3)
/// - Local filter reduces token ingress (Quality Gate)
/// - Privacy classification enforcement
/// </summary>
public class LocalFilterTests
{
    private readonly LocalFilter _filter = new();

    // --- Privacy class enforcement ---

    [Fact]
    public void Private_Data_Is_Blocked()
    {
        var evt = new RawEvent
        {
            PrivacyClass = "private",
            Text = "User's private clipboard content",
            Source = "clipboard"
        };

        var result = _filter.Filter(evt);

        Assert.False(result.IsAllowed);
        Assert.Empty(result.FilteredPayload);
        Assert.Contains("Private", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sensitive_Data_Is_Blocked()
    {
        var evt = new RawEvent
        {
            PrivacyClass = "sensitive",
            Text = "password=abc123",
            Source = "clipboard"
        };

        var result = _filter.Filter(evt);

        Assert.False(result.IsAllowed);
        Assert.Empty(result.FilteredPayload);
    }

    [Fact]
    public void Public_Data_Is_Allowed()
    {
        var evt = new RawEvent
        {
            PrivacyClass = "public",
            Text = "Project meeting notes for Q3 planning",
            Source = "file",
            EventType = "file_change",
            ActiveWindow = "VS Code"
        };

        var result = _filter.Filter(evt);

        Assert.True(result.IsAllowed);
        Assert.NotEmpty(result.FilteredPayload);
    }

    [Fact]
    public void Internal_Data_Is_Allowed()
    {
        var evt = new RawEvent
        {
            PrivacyClass = "internal",
            Text = "Internal project status update",
            Source = "file",
            EventType = "file_change"
        };

        var result = _filter.Filter(evt);

        Assert.True(result.IsAllowed);
        Assert.NotEmpty(result.FilteredPayload);
    }

    // --- Default privacy is restrictive ---

    [Fact]
    public void Unknown_Privacy_Class_Defaults_To_Private()
    {
        var evt = new RawEvent
        {
            PrivacyClass = "unknown_value",
            Text = "Some data",
            Source = "file"
        };

        var result = _filter.Filter(evt);

        Assert.False(result.IsAllowed);
    }

    // --- PII stripping ---

    [Fact]
    public void Email_Addresses_Are_Redacted()
    {
        var evt = new RawEvent
        {
            PrivacyClass = "public",
            Text = "Contact user@example.com for details",
            Source = "file",
            EventType = "file_change"
        };

        var result = _filter.Filter(evt);

        Assert.True(result.IsAllowed);
        Assert.DoesNotContain("user@example.com", result.FilteredPayload);
        Assert.Contains("[EMAIL]", result.FilteredPayload);
    }

    [Fact]
    public void Phone_Numbers_Are_Redacted()
    {
        var evt = new RawEvent
        {
            PrivacyClass = "public",
            Text = "Call 555-123-4567 for info",
            Source = "file",
            EventType = "file_change"
        };

        var result = _filter.Filter(evt);

        Assert.True(result.IsAllowed);
        Assert.DoesNotContain("555-123-4567", result.FilteredPayload);
        Assert.Contains("[PHONE]", result.FilteredPayload);
    }

    [Fact]
    public void Long_Base64_Tokens_Are_Redacted()
    {
        var token = new string('A', 50); // 50+ char base64-like string
        var evt = new RawEvent
        {
            PrivacyClass = "public",
            Text = $"Token: {token} end",
            Source = "file",
            EventType = "file_change"
        };

        var result = _filter.Filter(evt);

        Assert.True(result.IsAllowed);
        Assert.DoesNotContain(token, result.FilteredPayload);
    }

    // --- Size reduction ---

    [Fact]
    public void Filtered_Payload_Is_Smaller_Than_Original()
    {
        var longText = string.Join(" ", Enumerable.Repeat("Meeting with user@example.com at 555-123-4567 about project Alpha", 20));
        var evt = new RawEvent
        {
            PrivacyClass = "public",
            Text = longText,
            Source = "file",
            EventType = "file_change"
        };

        var result = _filter.Filter(evt);

        Assert.True(result.IsAllowed);
        Assert.True(result.FilteredSize < result.OriginalSize);
        Assert.True(result.ReductionRatio > 0);
    }

    // --- Summary format ---

    [Fact]
    public void Filtered_Payload_Includes_Source_Metadata()
    {
        var evt = new RawEvent
        {
            PrivacyClass = "public",
            Text = "Project update",
            Source = "file_watcher",
            EventType = "file_change",
            ActiveWindow = "VS Code"
        };

        var result = _filter.Filter(evt);

        Assert.Contains("source=file_watcher", result.FilteredPayload);
        Assert.Contains("type=file_change", result.FilteredPayload);
        Assert.Contains("window=VS Code", result.FilteredPayload);
    }

    [Fact]
    public void Long_Text_Is_Truncated_In_Summary()
    {
        // Use text with spaces to avoid triggering the token regex
        var longText = string.Join(" ", Enumerable.Repeat("meeting notes about the project schedule and deliverables for Q3 planning", 30));
        var evt = new RawEvent
        {
            PrivacyClass = "public",
            Text = longText,
            Source = "file",
            EventType = "file_change"
        };

        var result = _filter.Filter(evt);

        Assert.True(result.FilteredPayload.Length < result.OriginalSize);
        Assert.Contains("...", result.FilteredPayload);
    }

    // --- FilterText method ---

    [Fact]
    public void FilterText_Private_Blocks()
    {
        var result = _filter.FilterText("secret data", PrivacyClass.Private);

        Assert.False(result.IsAllowed);
        Assert.Empty(result.FilteredPayload);
    }

    [Fact]
    public void FilterText_Public_Allows()
    {
        var result = _filter.FilterText("public meeting notes", PrivacyClass.Public);

        Assert.True(result.IsAllowed);
        Assert.NotEmpty(result.FilteredPayload);
    }

    [Fact]
    public void FilterText_Strips_Emails()
    {
        var result = _filter.FilterText("Contact john@doe.com", PrivacyClass.Public);

        Assert.True(result.IsAllowed);
        Assert.DoesNotContain("john@doe.com", result.FilteredPayload);
        Assert.Contains("[EMAIL]", result.FilteredPayload);
    }

    // --- Null handling ---

    [Fact]
    public void Filter_Null_RawEvent_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _filter.Filter(null!));
    }

    [Fact]
    public void FilterText_Null_Text_Handled()
    {
        var result = _filter.FilterText(null!, PrivacyClass.Public);

        Assert.True(result.IsAllowed);
        Assert.NotNull(result.FilteredPayload);
    }

    // --- Size tracking ---

    [Fact]
    public void Original_Size_Tracked()
    {
        var evt = new RawEvent
        {
            PrivacyClass = "public",
            Text = "Hello world",
            Source = "file",
            EventType = "file_change"
        };

        var result = _filter.Filter(evt);

        Assert.Equal(11, result.OriginalSize);
    }

    [Fact]
    public void Blocked_Data_Has_Zero_Filtered_Size()
    {
        var evt = new RawEvent
        {
            PrivacyClass = "private",
            Text = "Secret content here",
            Source = "clipboard"
        };

        var result = _filter.Filter(evt);

        Assert.Equal(0, result.FilteredSize);
    }
}
