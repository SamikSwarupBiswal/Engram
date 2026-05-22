using System;
using System.IO;
using System.Threading.Tasks;
using Engram.Store.Automation;
using Xunit;

namespace Engram.Store.Tests;

public class TelemetryAndTimelineTests : IDisposable
{
    private readonly string _tempDir;

    public TelemetryAndTimelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"engram_telemetry_tests_{Guid.NewGuid():n}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public void ExecutionTelemetryEngine_RecordsAndSummarizesCorrectly()
    {
        // Arrange
        var engine = new ExecutionTelemetryEngine(_tempDir);

        // Act
        engine.RecordWorkflowMetrics(
            workflowId: "wf-1",
            success: true,
            duration: TimeSpan.FromSeconds(10),
            retryCount: 1,
            recoverySuccess: true,
            interventions: 0,
            abandoned: false
        );

        engine.RecordWorkflowMetrics(
            workflowId: "wf-2",
            success: false,
            duration: TimeSpan.FromSeconds(20),
            retryCount: 2,
            recoverySuccess: false,
            interventions: 2,
            abandoned: true
        );

        // Assert
        var summary = engine.GetSummary();
        Assert.Equal(0.5, summary.SuccessRate);
        Assert.Equal(3, summary.RetryFrequency);
        Assert.Equal(1, summary.FailureCount);
        Assert.Equal(0.5, summary.RecoverySuccessRate); // 1 recovery success out of 2 attempts
        Assert.Equal(TimeSpan.FromSeconds(15), summary.AverageLatency);
        Assert.Equal(2, summary.HumanInterventions);
        Assert.Equal(0.5, summary.WorkflowAbandonmentRate);

        // Verify files are written
        Assert.True(File.Exists(Path.Combine(_tempDir, "automation", "telemetry", "wf-1.json")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "automation", "telemetry", "wf-2.json")));
    }

    [Fact]
    public void OperationalTimeline_RecordsAndRetrievesEventsInJsonl()
    {
        // Arrange
        var timeline = new OperationalTimeline(_tempDir);
        var workflowId = "wf-123";

        // Act
        timeline.RecordEvent(workflowId, "workflow.start", "Started workflow 123");
        timeline.RecordEvent(workflowId, "step.execute", "Executing step 1");

        // Assert
        var events = timeline.GetEvents(workflowId);
        Assert.Equal(2, events.Count);
        
        Assert.Equal("workflow.start", events[0].EventType);
        Assert.Equal("Started workflow 123", events[0].Description);
        Assert.Equal(workflowId, events[0].WorkflowId);

        Assert.Equal("step.execute", events[1].EventType);
        Assert.Equal("Executing step 1", events[1].Description);
        Assert.Equal(workflowId, events[1].WorkflowId);

        // Verify jsonl file contains two lines
        var filePath = Path.Combine(_tempDir, "automation", "timeline", $"{workflowId}.jsonl");
        Assert.True(File.Exists(filePath));
        var lines = File.ReadAllLines(filePath);
        Assert.Equal(2, lines.Length);
    }
}
