using Engram.Store.Validation;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Real-world validation tests.
/// Tests against path traversal, empty inputs, oversized data, Unicode attacks.
/// </summary>
public class InputValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateRootPath_RejectsEmpty(string? root)
    {
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateRootPath(root!));
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("/tmp/.engram/../../secret")]
    [InlineData("C:\\\\Users\\\\..\\\\Windows")]
    public void ValidateRootPath_RejectsTraversal(string root)
    {
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateRootPath(root));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateEventId_RejectsEmpty(string? id)
    {
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateEventId(id!));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("path/traversal")]
    [InlineData("back\\slash")]
    public void ValidateEventId_RejectsPathChars(string id)
    {
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateEventId(id));
    }

    [Fact]
    public void ValidateEventId_RejectsTooLong()
    {
        var longId = new string('a', 300);
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateEventId(longId));
    }

    [Fact]
    public void ValidateTextSize_RejectsOversized()
    {
        // 11MB string
        var bigText = new string('x', 11 * 1024 * 1024);
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateTextSize(bigText));
    }

    [Fact]
    public void ValidateTextSize_AcceptsNull()
    {
        InputValidator.ValidateTextSize(null); // should not throw
    }

    [Fact]
    public void ValidateTextSize_AcceptsNormalSize()
    {
        InputValidator.ValidateTextSize("Hello, Engram!");
    }

    [Fact]
    public void ValidateRawEvent_RejectsEmptyEventType()
    {
        var evt = TestEvents.Create();
        evt.EventType = "";
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateRawEvent(evt));
    }

    [Fact]
    public void ValidateRawEvent_RejectsEmptySource()
    {
        var evt = TestEvents.Create();
        evt.Source = "";
        Assert.Throws<EngramValidationException>(() => InputValidator.ValidateRawEvent(evt));
    }

    [Fact]
    public void ValidateRawEvent_AcceptsUnicodeContent()
    {
        var evt = TestEvents.Create(text: "日本語テスト 🚀 مرحبا");
        InputValidator.ValidateRawEvent(evt); // should not throw
    }

    [Fact]
    public void ValidateRawEvent_AcceptsEmojiInEventType()
    {
        var evt = TestEvents.Create(eventType: "screen_capture_📸");
        InputValidator.ValidateRawEvent(evt); // should not throw
    }
}
