using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Engram.Store.Events;
using Engram.Store.Perception;

namespace Engram.Store.Reality;

/// <summary>
/// Temporal Fusion Engine merges the active operating systems events, active workflows,
/// active documents, browser state, and classified scene into a unified temporal chronology entry.
/// </summary>
public class TemporalFusionEngine : IDisposable
{
    private readonly CrossModalResolver _resolver;
    private readonly IEventBus _eventBus;
    
    private readonly object _lock = new();
    private readonly List<IDisposable> _subscriptions = new();
    
    // Internal States
    private string _activeWorkflowId = string.Empty;
    private string _windowProcess = string.Empty;
    private string _windowTitle = string.Empty;
    private string _activeDocumentPath = string.Empty;
    private int _browserTabCount = 0;
    private string _currentScene = "unknown";
    private string _focusReason = "unknown";
    private readonly Dictionary<string, string> _metadata = new();
    
    private FusedChronologyEntry? _lastFused;

    public TemporalFusionEngine(CrossModalResolver resolver, IEventBus eventBus)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        
        SubscribeToEvents();
    }

    public FusedChronologyEntry? LastFusedEntry
    {
        get
        {
            lock (_lock) return _lastFused;
        }
    }

    private void SubscribeToEvents()
    {
        // 1. Subscribe to active window changes
        _subscriptions.Add(_eventBus.Subscribe("perception.active_window_changed", envelope =>
        {
            if (envelope.Payload == null) return;
            
            // Try to extract via dynamic or JSON serialization/deserialization to be robust
            try
            {
                var payloadStr = System.Text.Json.JsonSerializer.Serialize(envelope.Payload);
                var payload = System.Text.Json.JsonSerializer.Deserialize<WindowChangedPayload>(payloadStr);
                if (payload != null)
                {
                    lock (_lock)
                    {
                        _windowProcess = payload.Process;
                        _windowTitle = payload.Title;
                        RunFusion();
                    }
                }
            }
            catch { }
        }));

        // 2. Subscribe to file changes to update active document
        _subscriptions.Add(_eventBus.Subscribe("perception.file_changed", envelope => HandleFileEvent(envelope.Payload)));
        _subscriptions.Add(_eventBus.Subscribe("perception.file_created", envelope => HandleFileEvent(envelope.Payload)));

        // 3. Subscribe to operational world model updates (for workflows and browser tab count)
        _subscriptions.Add(_eventBus.Subscribe("automation.worldmodel.changed", envelope =>
        {
            if (envelope.Payload == null) return;
            try
            {
                var payloadStr = System.Text.Json.JsonSerializer.Serialize(envelope.Payload);
                var payload = System.Text.Json.JsonSerializer.Deserialize<WorldModelChangedPayload>(payloadStr);
                if (payload != null)
                {
                    lock (_lock)
                    {
                        if (string.Equals(payload.Property, "ActiveWorkflow", StringComparison.OrdinalIgnoreCase))
                        {
                            _activeWorkflowId = payload.Value?.ToString() ?? string.Empty;
                        }
                        else if (string.Equals(payload.Property, "ActiveDocument", StringComparison.OrdinalIgnoreCase))
                        {
                            _activeDocumentPath = payload.Value?.ToString() ?? string.Empty;
                        }
                        else if (string.Equals(payload.Property, "BrowserTabsCount", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(payload.Value?.ToString(), out var count))
                            {
                                _browserTabCount = count;
                            }
                        }
                        RunFusion();
                    }
                }
            }
            catch { }
        }));

        // 4. Subscribe to scene constructor updates
        _subscriptions.Add(_eventBus.Subscribe("reality.scene_changed", envelope =>
        {
            if (envelope.Payload == null) return;
            try
            {
                var sceneName = envelope.Payload.ToString();
                if (!string.IsNullOrEmpty(sceneName))
                {
                    lock (_lock)
                    {
                        _currentScene = sceneName;
                        RunFusion();
                    }
                }
            }
            catch { }
        }));

        // 5. Subscribe to idle transitions
        _subscriptions.Add(_eventBus.Subscribe("perception.idle_transition", envelope =>
        {
            lock (_lock)
            {
                _windowProcess = "idle";
                _windowTitle = "User Idle";
                RunFusion();
            }
        }));
    }

    private void HandleFileEvent(object? payloadObj)
    {
        if (payloadObj == null) return;
        try
        {
            var payloadStr = System.Text.Json.JsonSerializer.Serialize(payloadObj);
            var fileEvent = System.Text.Json.JsonSerializer.Deserialize<SemanticFileEvent>(payloadStr);
            if (fileEvent != null)
            {
                lock (_lock)
                {
                    _activeDocumentPath = fileEvent.FilePath;
                    RunFusion();
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Forces a fusion step and returns the resulting fused entry.
    /// </summary>
    public FusedChronologyEntry ForceFusion(string? focusReason = null)
    {
        lock (_lock)
        {
            if (focusReason != null)
            {
                _focusReason = focusReason;
            }
            RunFusion(forcePublish: true);
            return _lastFused!;
        }
    }

    private void RunFusion(bool forcePublish = false)
    {
        // Resolve current context to canonical WikiNode ID
        string? resolvedNodeId = null;

        // Priority 1: Active Document Path
        if (!string.IsNullOrEmpty(_activeDocumentPath))
        {
            resolvedNodeId = _resolver.ResolvePath(_activeDocumentPath);
        }

        // Priority 2: Active Window Title or Process
        if (string.IsNullOrEmpty(resolvedNodeId) && !string.IsNullOrEmpty(_windowTitle))
        {
            resolvedNodeId = _resolver.ResolveWindow(_windowTitle);
        }
        if (string.IsNullOrEmpty(resolvedNodeId) && !string.IsNullOrEmpty(_windowProcess))
        {
            resolvedNodeId = _resolver.ResolveProcess(_windowProcess);
        }

        // Priority 3: Active Workflow
        if (string.IsNullOrEmpty(resolvedNodeId) && !string.IsNullOrEmpty(_activeWorkflowId))
        {
            resolvedNodeId = _resolver.ResolveAlias(_activeWorkflowId);
        }

        var entry = new FusedChronologyEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            WorkflowId = _activeWorkflowId,
            WindowProcess = _windowProcess,
            WindowTitle = _windowTitle,
            ActiveDocumentPath = _activeDocumentPath,
            BrowserTabCount = _browserTabCount,
            Scene = _currentScene,
            FocusReason = _focusReason,
            ResolvedNodeId = resolvedNodeId,
            Metadata = new Dictionary<string, string>(_metadata)
        };

        bool changed = _lastFused == null ||
                      _lastFused.WorkflowId != entry.WorkflowId ||
                      _lastFused.WindowProcess != entry.WindowProcess ||
                      _lastFused.WindowTitle != entry.WindowTitle ||
                      _lastFused.ActiveDocumentPath != entry.ActiveDocumentPath ||
                      _lastFused.BrowserTabCount != entry.BrowserTabCount ||
                      _lastFused.Scene != entry.Scene ||
                      _lastFused.ResolvedNodeId != entry.ResolvedNodeId ||
                      _lastFused.FocusReason != entry.FocusReason;

        if (changed || forcePublish)
        {
            _lastFused = entry;
            _eventBus.Publish(new EventEnvelope
            {
                EventType = "reality.temporal_fused",
                Source = "temporal_fusion_engine",
                Payload = entry
            });
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }
            _subscriptions.Clear();
        }
    }

    private class WindowChangedPayload
    {
        public string Process { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    private class WorldModelChangedPayload
    {
        public string Property { get; set; } = string.Empty;
        public object? Value { get; set; }
    }
}

/// <summary>
/// A fused entry representing the state of reality at a single moment in time.
/// </summary>
public class FusedChronologyEntry
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string WorkflowId { get; set; } = string.Empty;
    public string WindowProcess { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public string ActiveDocumentPath { get; set; } = string.Empty;
    public int BrowserTabCount { get; set; }
    public string Scene { get; set; } = string.Empty;
    public string FocusReason { get; set; } = string.Empty;
    public string? ResolvedNodeId { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
