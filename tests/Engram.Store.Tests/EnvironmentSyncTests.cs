using System;
using System.Collections.Generic;
using Engram.Store.Automation;
using Engram.Store.Events;
using Xunit;

using ExecutionContext = Engram.Store.Automation.ExecutionContext;

namespace Engram.Store.Tests;

public class EnvironmentSyncTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly InMemoryEventBus _eventBus = new();
    private readonly OperationalWorldModel _worldModel;

    public EnvironmentSyncTests()
    {
        _worldModel = new OperationalWorldModel(_eventBus);
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }

    [Fact]
    public void CheckSynchronization_WithNoDivergences_ReturnsSynchronizedReport()
    {
        var engine = new EnvironmentSynchronizationEngine(_worldModel, _eventBus);
        var context = new ExecutionContext();

        var report = engine.CheckSynchronization(context);

        Assert.True(report.IsSynchronized);
        Assert.Empty(report.Divergences);
    }

    [Fact]
    public void CheckSynchronization_WithActiveWorkflowDivergence_DetectsHighSeverityDivergence()
    {
        var engine = new EnvironmentSynchronizationEngine(_worldModel, _eventBus);
        var context = new ExecutionContext();

        // 1. Context expects workflow w1
        context.SetVariable("active_workflow_id", "w1");

        // 2. WorldModel is empty or on w2
        _worldModel.UpdateState("Running", "w2", "", 0, new Dictionary<string, string>());

        bool eventFired = false;
        _eventBus.Subscribe("automation.environment.desynchronized", env =>
        {
            eventFired = true;
        });

        var report = engine.CheckSynchronization(context);

        Assert.False(report.IsSynchronized);
        Assert.Single(report.Divergences);
        Assert.Equal("Workflow", report.Divergences[0].Source);
        Assert.Equal("w1", report.Divergences[0].Expected);
        Assert.Equal("w2", report.Divergences[0].Actual);
        Assert.Equal("High", report.Divergences[0].Severity);
        Assert.True(eventFired);
    }

    [Fact]
    public void CheckSynchronization_WithBrowserAndDocumentDivergence_DetectsMultipleDivergences()
    {
        var engine = new EnvironmentSynchronizationEngine(_worldModel, _eventBus);
        var context = new ExecutionContext();

        // Context expects a browser session and report.docx
        context.SetVariable("requires_browser_active", true);
        context.SetVariable("expected_active_document", "report.docx");

        // WorldModel shows 0 tabs and no active doc
        _worldModel.UpdateState("Running", "w1", "", 0, new Dictionary<string, string>());

        var report = engine.CheckSynchronization(context);

        Assert.False(report.IsSynchronized);
        Assert.Equal(2, report.Divergences.Count);
        
        Assert.Contains(report.Divergences, d => d.Source == "Browser" && d.Severity == "High");
        Assert.Contains(report.Divergences, d => d.Source == "WorldModel" && d.Severity == "Medium");
    }

    [Fact]
    public void CheckSynchronization_WithNetworkOfflineDivergence_DetectsCriticalDivergence()
    {
        var engine = new EnvironmentSynchronizationEngine(_worldModel, _eventBus);
        var context = new ExecutionContext();

        // Context requires network
        context.SetVariable("requires_network_online", true);

        // WorldModel is offline
        _worldModel.UpdateState("Running", "w1", "", 0, new Dictionary<string, string>
        {
            ["network_offline"] = "True"
        });

        var report = engine.CheckSynchronization(context);

        Assert.False(report.IsSynchronized);
        Assert.Single(report.Divergences);
        Assert.Equal("Desktop", report.Divergences[0].Source);
        Assert.Equal("Critical", report.Divergences[0].Severity);
    }
}
