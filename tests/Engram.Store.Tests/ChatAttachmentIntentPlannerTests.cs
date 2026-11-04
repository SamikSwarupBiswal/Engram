using System.Text.Json;
using Engram.Store.Agent;
using Engram.Store.Inference;

namespace Engram.Store.Tests;

public sealed class ChatAttachmentIntentPlannerTests
{
    [Fact]
    public async Task ModelChoosesOrderUsingConversationAndCurrentFileSlots()
    {
        var planner = new ChatAttachmentIntentPlanner((messages, _, _) =>
        {
            using var payload = JsonDocument.Parse(messages[1].Content);
            var conversation = payload.RootElement.GetProperty("conversation");
            Assert.Equal("Which one comes first?", conversation[1].GetProperty("content").GetString());
            Assert.Equal("The second one, then the first.", conversation[2].GetProperty("content").GetString());
            Assert.Equal(2, payload.RootElement.GetProperty("files").GetArrayLength());
            return Task.FromResult(Response("""{"tool":"pdf.merge","files":[2,1]}"""));
        });

        var result = await planner.InterpretAsync([
            new() { Content = "Join these documents into one." },
            new() { Role = "assistant", Content = "Which one comes first?" },
            new() { Content = "The second one, then the first." }
        ], ["application/pdf", "application/pdf"]);

        var call = Assert.IsType<AttachmentToolCall>(result.Call);
        Assert.Equal("pdf.merge", call.Tool);
        Assert.Equal(new[] { 2, 1 }, call.Files);
        Assert.Equal("test-model", result.Model);
        Assert.Equal("test-provider", result.Provider);
    }

    [Theory]
    [InlineData("""{"tool":"desktop.run","files":[1]}""")]
    [InlineData("""{"tool":"pdf.extract_summary","files":[2]}""")]
    [InlineData("""{"tool":"pdf.merge","files":[1,1]}""")]
    [InlineData("""{"tool":"pdf.extract_summary","files":["1"]}""")]
    [InlineData("""{"tool":"pdf.merge","Tool":"pdf.extract_summary","files":[1]}""")]
    [InlineData("""{"tool":"pdf.extract_summary","files":[1],"permission":"approved"}""")]
    [InlineData("""{"tool":"pdf.extract_summary","files":[1],"outputPath":"C:\\unrequested.docx"}""")]
    public async Task UntrustedModelOutputCannotSupplyUnavailableToolsFilesOrPermissions(string json)
    {
        var planner = new ChatAttachmentIntentPlanner((_, _, _) => Task.FromResult(Response(json)));
        var result = await planner.InterpretAsync([new() { Content = "Make a cited summary." }], ["application/pdf"]);

        Assert.Null(result.Call);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task ModelUnavailabilityReturnsNoToolEvenForAnExactOperationRequest()
    {
        var planner = new ChatAttachmentIntentPlanner((_, _, _) => Task.FromResult(InferenceResult.Failed("offline")));
        var result = await planner.InterpretAsync([new() { Content = "Merge the attached PDFs." }], ["application/pdf", "application/pdf"]);

        Assert.Null(result.Call);
        Assert.Null(result.Model);
        Assert.Contains("unavailable", result.Message);
    }

    [Theory]
    [InlineData("user", true)]
    [InlineData("assistant", false)]
    public async Task FollowUpRetainsExplicitUserDestinationButCannotAdoptAnAssistantPath(string role, bool accepted)
    {
        var path = Path.Combine(Path.GetTempPath(), "combined.pdf");
        var planner = new ChatAttachmentIntentPlanner((_, _, _) => Task.FromResult(Response(
            JsonSerializer.Serialize(new { tool = "pdf.merge", files = new[] { 2, 1 }, outputPath = path }))));

        var result = await planner.InterpretAsync([
            new() { Role = role, Content = $"Put the combined file at {path}." },
            new() { Role = "assistant", Content = "Which document comes first?" },
            new() { Content = "Start with the second one." }
        ], ["application/pdf", "application/pdf"]);

        if (accepted)
            Assert.Equal(path, Assert.IsType<AttachmentToolCall>(result.Call).OutputPath);
        else
            Assert.Null(result.Call);
    }

    [Fact]
    public async Task CancelledRequestDoesNotCallTheModel()
    {
        var planner = new ChatAttachmentIntentPlanner((_, _, _) => throw new InvalidOperationException("Model was called"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => planner.InterpretAsync(
            [new() { Content = "Combine the attachments." }], ["application/pdf"], new CancellationToken(true)));
    }

    private static InferenceResult Response(string json) => new()
    {
        Success = true,
        Content = json,
        Model = "test-model",
        Provider = "test-provider"
    };
}
