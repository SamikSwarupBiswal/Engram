using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Engram.Store.Events;

namespace Engram.Store.Automation;

public class EnvironmentDivergence
{
    public string Source { get; set; } = string.Empty; // WorldModel, Browser, Desktop, Workflow
    public string Expected { get; set; } = string.Empty;
    public string Actual { get; set; } = string.Empty;
    public string Severity { get; set; } = "Low";
}

public class EnvironmentSyncReport
{
    public bool IsSynchronized { get; set; } = true;
    public List<EnvironmentDivergence> Divergences { get; set; } = new();
}

public class EnvironmentSynchronizationEngine
{
    private readonly OperationalWorldModel _worldModel;
    private readonly IEventBus _eventBus;
    private readonly ILogger? _logger;

    public EnvironmentSynchronizationEngine(
        OperationalWorldModel worldModel,
        IEventBus eventBus,
        ILogger? logger = null)
    {
        _worldModel = worldModel ?? throw new ArgumentNullException(nameof(worldModel));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger;
    }

    public EnvironmentSyncReport CheckSynchronization(ExecutionContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var report = new EnvironmentSyncReport();

        // 1. Verify Active Workflow alignment
        var expectedWorkflow = context.GetVariable<string>("active_workflow_id") ?? string.Empty;
        var actualWorkflow = _worldModel.ActiveWorkflow;
        if (!string.IsNullOrEmpty(expectedWorkflow) && expectedWorkflow != actualWorkflow)
        {
            report.Divergences.Add(new EnvironmentDivergence
            {
                Source = "Workflow",
                Expected = expectedWorkflow,
                Actual = actualWorkflow,
                Severity = "High"
            });
        }

        // 2. Verify Active Document alignment
        var expectedDoc = context.GetVariable<string>("expected_active_document") ?? string.Empty;
        var actualDoc = _worldModel.ActiveDocument;
        if (!string.IsNullOrEmpty(expectedDoc) && expectedDoc != actualDoc)
        {
            report.Divergences.Add(new EnvironmentDivergence
            {
                Source = "WorldModel",
                Expected = expectedDoc,
                Actual = actualDoc,
                Severity = "Medium"
            });
        }

        // 3. Verify Browser environment alignment
        var expectsBrowser = context.GetVariable<bool>("requires_browser_active");
        if (expectsBrowser && _worldModel.BrowserTabsCount == 0)
        {
            report.Divergences.Add(new EnvironmentDivergence
            {
                Source = "Browser",
                Expected = "TabsCount > 0",
                Actual = "TabsCount = 0",
                Severity = "High"
            });
        }

        // 4. Verify offline constraint matches reality
        bool expectsOnline = context.GetVariable<bool>("requires_network_online");
        if (expectsOnline && _worldModel.EnvironmentalConstraints.ContainsKey("network_offline"))
        {
            report.Divergences.Add(new EnvironmentDivergence
            {
                Source = "Desktop",
                Expected = "NetworkAvailable",
                Actual = "NetworkOffline",
                Severity = "Critical"
            });
        }

        if (report.Divergences.Count > 0)
        {
            report.IsSynchronized = false;
            _logger?.LogWarning("Environment desynchronization detected: {Count} divergence(s).", report.Divergences.Count);

            _eventBus.Publish(new EventEnvelope
            {
                EventType = "automation.environment.desynchronized",
                Source = "environment_synchronization_engine",
                Payload = report
            });
        }

        return report;
    }
}
