using System;
using System.Collections.Generic;
using System.Linq;
using Engram.Store.Events;
using Engram.Store.Perception;

namespace Engram.Store.Reality;

/// <summary>
/// Subscribes to perceptual and operational events to classify the user's cognitive environment
/// into semantic scenes like BurnoutSpiral, CodingSession, FinancialWorkflow, ResearchArc, or ProjectMomentum.
/// Emits reality.scene_changed event when a new scene is detected.
/// </summary>
public class SemanticSceneConstructor : IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly object _lock = new();

    // Classification inputs
    private string _currentProcess = string.Empty;
    private string _currentTitle = string.Empty;
    private int _browserTabCount = 0;
    private int _interruptionCount = 0;
    private int _recentFileChanges = 0;
    private DateTimeOffset _lastFileChangeTime = DateTimeOffset.MinValue;
    private DateTimeOffset _currentFocusStarted = DateTimeOffset.UtcNow;
    private readonly List<DateTimeOffset> _recentWindowSwitches = new();

    private string _currentScene = "unknown";

    public SemanticSceneConstructor(IEventBus eventBus)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        SubscribeToEvents();
    }

    public string CurrentScene
    {
        get
        {
            lock (_lock) return _currentScene;
        }
    }

    // Exposed for testing
    public void SetState(string process, string title, int tabCount, int interruptionCount, int recentFileChanges, DateTimeOffset focusStarted)
    {
        lock (_lock)
        {
            _currentProcess = process;
            _currentTitle = title;
            _browserTabCount = tabCount;
            _interruptionCount = interruptionCount;
            _recentFileChanges = recentFileChanges;
            _currentFocusStarted = focusStarted;
        }
    }

    private void SubscribeToEvents()
    {
        // 1. Window changed event
        _subscriptions.Add(_eventBus.Subscribe("perception.active_window_changed", envelope =>
        {
            if (envelope.Payload == null) return;
            try
            {
                var payloadStr = System.Text.Json.JsonSerializer.Serialize(envelope.Payload);
                var payload = System.Text.Json.JsonSerializer.Deserialize<WindowPayload>(payloadStr);
                if (payload != null)
                {
                    lock (_lock)
                    {
                        var now = DateTimeOffset.UtcNow;
                        if (_currentProcess != payload.Process || _currentTitle != payload.Title)
                        {
                            _currentProcess = payload.Process;
                            _currentTitle = payload.Title;
                            _currentFocusStarted = now;
                            _recentWindowSwitches.Add(now);
                        }
                        
                        CleanWindowSwitches(now);
                        EvaluateAndPublishScene();
                    }
                }
            }
            catch { }
        }));

        // 2. File changes
        _subscriptions.Add(_eventBus.Subscribe("perception.file_changed", envelope => RecordFileChange()));
        _subscriptions.Add(_eventBus.Subscribe("perception.file_created", envelope => RecordFileChange()));

        // 3. Operational world model changes (tabs and interruption count)
        _subscriptions.Add(_eventBus.Subscribe("automation.worldmodel.changed", envelope =>
        {
            if (envelope.Payload == null) return;
            try
            {
                var payloadStr = System.Text.Json.JsonSerializer.Serialize(envelope.Payload);
                var payload = System.Text.Json.JsonSerializer.Deserialize<WorldModelPayload>(payloadStr);
                if (payload != null)
                {
                    lock (_lock)
                    {
                        if (string.Equals(payload.Property, "BrowserTabsCount", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(payload.Value?.ToString(), out var count))
                            {
                                _browserTabCount = count;
                            }
                        }
                        else if (string.Equals(payload.Property, "InterruptionCount", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(payload.Value?.ToString(), out var count))
                            {
                                _interruptionCount = count;
                            }
                        }
                        EvaluateAndPublishScene();
                    }
                }
            }
            catch { }
        }));
    }

    private void RecordFileChange()
    {
        lock (_lock)
        {
            _recentFileChanges++;
            _lastFileChangeTime = DateTimeOffset.UtcNow;
            EvaluateAndPublishScene();
        }
    }

    private void CleanWindowSwitches(DateTimeOffset now)
    {
        // Keep only switches within the last 2 minutes
        var cutoff = now.AddMinutes(-2);
        _recentWindowSwitches.RemoveAll(t => t < cutoff);
    }

    /// <summary>
    /// Evaluates the metrics and classifies the user context.
    /// </summary>
    public string Classify()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var processLower = _currentProcess.ToLowerInvariant();
            var titleLower = _currentTitle.ToLowerInvariant();
            var focusDuration = now - _currentFocusStarted;

            // 1. BurnoutSpiral
            // High switches in last 2 mins, or high interruption count, combined with browser/communication distraction
            bool isDistractingApp = processLower.Contains("chrome") || processLower.Contains("firefox") || 
                                    processLower.Contains("edge") || processLower.Contains("slack") || 
                                    processLower.Contains("discord");
            bool isDistractingTitle = titleLower.Contains("youtube") || titleLower.Contains("reddit") || 
                                      titleLower.Contains("twitter") || titleLower.Contains("facebook") || 
                                      titleLower.Contains("social");
            if ((_recentWindowSwitches.Count >= 6 || _interruptionCount > 5) && 
                (isDistractingApp || isDistractingTitle) && 
                focusDuration.TotalMinutes < 2.0)
            {
                return "BurnoutSpiral";
            }

            // 2. FinancialWorkflow
            // Payment systems, stripe checkout, invoice management
            if (titleLower.Contains("billing") || titleLower.Contains("payment") || 
                titleLower.Contains("checkout") || titleLower.Contains("invoice") || 
                titleLower.Contains("stripe") || titleLower.Contains("paypal") || 
                titleLower.Contains("pricing"))
            {
                return "FinancialWorkflow";
            }

            // 3. CodingSession
            // Active editor/IDE window, recent file modifications
            bool isIDE = processLower.Contains("code") || processLower.Contains("visual studio") || 
                         processLower.Contains("devenv") || processLower.Contains("intellij") || 
                         processLower.Contains("rider") || processLower.Contains("vim") ||
                         processLower.Contains("sublime");
            bool fileChangedRecently = (now - _lastFileChangeTime).TotalMinutes <= 5.0 || _recentFileChanges > 0;
            if (isIDE && (fileChangedRecently || focusDuration.TotalMinutes > 1.0))
            {
                // Upgrade to ProjectMomentum if we have been focusing without context switching
                if (focusDuration.TotalMinutes >= 10.0 && _recentWindowSwitches.Count <= 2)
                {
                    return "ProjectMomentum";
                }
                return "CodingSession";
            }

            // 4. ResearchArc
            // High tab count, browser with scientific, wiki, or doc titles
            bool isBrowser = processLower.Contains("chrome") || processLower.Contains("firefox") || 
                             processLower.Contains("edge") || processLower.Contains("safari");
            bool isResearchTitle = titleLower.Contains("search") || titleLower.Contains("wiki") || 
                                   titleLower.Contains("documentation") || titleLower.Contains("arxiv") || 
                                   titleLower.Contains("paper") || titleLower.Contains("scholar") || 
                                   titleLower.Contains("github") || titleLower.Contains("stackoverflow");
            if (_browserTabCount >= 15 || (isBrowser && isResearchTitle))
            {
                return "ResearchArc";
            }

            // 5. ProjectMomentum
            // Long focus duration (10+ min) on a work window (IDE or terminal)
            bool isWorkWindow = isIDE || processLower.Contains("terminal") || processLower.Contains("cmd") || 
                                processLower.Contains("powershell") || processLower.Contains("wt") ||
                                processLower.Contains("bash");
            if (focusDuration.TotalMinutes >= 10.0 && isWorkWindow && _recentWindowSwitches.Count <= 2)
            {
                return "ProjectMomentum";
            }

            return "unknown";
        }
    }

    public void EvaluateAndPublishScene()
    {
        lock (_lock)
        {
            var newScene = Classify();
            if (newScene != _currentScene)
            {
                var oldScene = _currentScene;
                _currentScene = newScene;

                _eventBus.Publish(new EventEnvelope
                {
                    EventType = "reality.scene_changed",
                    Source = "semantic_scene_constructor",
                    Payload = newScene
                });
            }
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

    private class WindowPayload
    {
        public string Process { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    private class WorldModelPayload
    {
        public string Property { get; set; } = string.Empty;
        public object? Value { get; set; }
    }
}
