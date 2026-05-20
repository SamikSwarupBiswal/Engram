using Engram.Store.Events;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// Environment model — Engram's understanding of the machine state.
/// 
/// NOT reading every file.
/// Instead understanding:
/// - workflows
/// - focus
/// - projects
/// - trajectories
/// - context
/// - priorities
/// - recurring behaviors
/// 
/// This enables true contextual cognition.
/// </summary>
public class EnvironmentModel
{
    private readonly IEventBus _eventBus;
    private readonly IBehavioralModeStrategy _modeStrategy;
    private readonly ILogger<EnvironmentModel>? _logger;

    private readonly object _lock = new();
    private readonly List<BehavioralMode> _behavioralHistory = new();
    private readonly Dictionary<string, AppUsage> _appUsage = new();
    private readonly List<ProjectActivity> _projectActivities = new();

    private string _currentBehavioralMode = "unknown";
    private string _currentPrimaryProject = string.Empty;
    private DateTimeOffset _lastModeTransition = DateTimeOffset.UtcNow;

    public EnvironmentModel(
        IEventBus eventBus,
        IBehavioralModeStrategy? modeStrategy = null,
        ILogger<EnvironmentModel>? logger = null)
    {
        _eventBus = eventBus;
        _modeStrategy = modeStrategy ?? new DefaultBehavioralModeStrategy();
        _logger = logger;
    }

    /// <summary>
    /// Process a window change event and update the environment model.
    /// </summary>
    public void ProcessWindowChange(string processName, string windowTitle, TimeSpan focusDuration)
    {
        lock (_lock)
        {
            // Update app usage
            if (!_appUsage.TryGetValue(processName, out var usage))
            {
                usage = new AppUsage { ProcessName = processName };
                _appUsage[processName] = usage;
            }
            usage.TotalTime += focusDuration;
            usage.LastUsed = DateTimeOffset.UtcNow;
            usage.UseCount++;

            // Update project activity
            var project = ExtractProjectFromTitle(windowTitle);
            if (!string.IsNullOrEmpty(project))
            {
                UpdateProjectActivity(project, focusDuration);
                _currentPrimaryProject = project;
            }

            // Detect behavioral mode via strategy (injectable for replay)
            var newMode = _modeStrategy.DetectMode(processName, windowTitle, focusDuration);
            if (newMode != _currentBehavioralMode)
            {
                TransitionBehavioralMode(newMode);
            }
        }
    }

    /// <summary>
    /// Process a file change event.
    /// </summary>
    public void ProcessFileChange(SemanticFileEvent fileEvent)
    {
        lock (_lock)
        {
            if (fileEvent.Category == "source_code" || fileEvent.Category == "project_config")
            {
                var project = fileEvent.Directory;
                if (!string.IsNullOrEmpty(project))
                {
                    UpdateProjectActivity(project, TimeSpan.Zero);
                }
            }
        }
    }

    /// <summary>
    /// Get the current environment state.
    /// </summary>
    public EnvironmentState GetCurrentState()
    {
        lock (_lock)
        {
            return new EnvironmentState
            {
                Timestamp = DateTimeOffset.UtcNow,
                CurrentBehavioralMode = _currentBehavioralMode,
                CurrentPrimaryProject = _currentPrimaryProject,
                TopApps = _appUsage.Values
                    .OrderByDescending(a => a.TotalTime)
                    .Take(5)
                    .Select(a => new AppSummary
                    {
                        ProcessName = a.ProcessName,
                        TotalTime = a.TotalTime,
                        UseCount = a.UseCount
                    })
                    .ToList(),
                ActiveProjects = _projectActivities
                    .Where(p => (DateTimeOffset.UtcNow - p.LastActivity).TotalHours < 24)
                    .OrderByDescending(p => p.LastActivity)
                    .Take(5)
                    .Select(p => new ProjectSummary
                    {
                        Name = p.Name,
                        LastActivity = p.LastActivity,
                        TotalTime = p.TotalTime
                    })
                    .ToList(),
                ModeTransitions = _behavioralHistory
                    .OrderByDescending(m => m.TransitionedAt)
                    .Take(10)
                    .ToList()
            };
        }
    }

    /// <summary>
    /// Get behavioral mode statistics.
    /// </summary>
    public Dictionary<string, TimeSpan> GetBehavioralModeDistribution(TimeSpan period)
    {
        lock (_lock)
        {
            var cutoff = DateTimeOffset.UtcNow - period;
            var modes = _behavioralHistory
                .Where(m => m.TransitionedAt >= cutoff)
                .ToList();

            var distribution = new Dictionary<string, TimeSpan>();
            for (int i = 0; i < modes.Count; i++)
            {
                var duration = i < modes.Count - 1
                    ? modes[i].TransitionedAt - modes[i + 1].TransitionedAt
                    : DateTimeOffset.UtcNow - modes[i].TransitionedAt;

                if (distribution.ContainsKey(modes[i].Mode))
                    distribution[modes[i].Mode] += duration;
                else
                    distribution[modes[i].Mode] = duration;
            }

            return distribution;
        }
    }

    private void TransitionBehavioralMode(string newMode)
    {
        var previousMode = _currentBehavioralMode;
        _currentBehavioralMode = newMode;
        _lastModeTransition = DateTimeOffset.UtcNow;

        _behavioralHistory.Add(new BehavioralMode
        {
            Mode = newMode,
            PreviousMode = previousMode,
            TransitionedAt = DateTimeOffset.UtcNow
        });

        // Keep only recent history
        while (_behavioralHistory.Count > 100)
            _behavioralHistory.RemoveAt(0);

        _eventBus.Publish(new EventEnvelope
        {
            EventType = "perception.behavioral_mode_changed",
            Source = "environment_model",
            Payload = new
            {
                NewMode = newMode,
                PreviousMode = previousMode,
                Timestamp = DateTimeOffset.UtcNow
            }
        });

        _logger?.LogInformation("Behavioral mode: {Previous} → {New}", previousMode, newMode);
    }

    private void UpdateProjectActivity(string projectName, TimeSpan duration)
    {
        var existing = _projectActivities.FirstOrDefault(p =>
            p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.TotalTime += duration;
            existing.LastActivity = DateTimeOffset.UtcNow;
        }
        else
        {
            _projectActivities.Add(new ProjectActivity
            {
                Name = projectName,
                TotalTime = duration,
                LastActivity = DateTimeOffset.UtcNow
            });
        }
    }

    private static string ExtractProjectFromTitle(string windowTitle)
    {
        // Extract project name from common window title patterns
        // "Engram - Visual Studio Code" → "Engram"
        // "project-name/src/file.cs at main · user/repo · GitHub" → "project-name"

        if (string.IsNullOrEmpty(windowTitle)) return string.Empty;

        // VSCode pattern: "filename - project - Visual Studio Code"
        if (windowTitle.Contains("Visual Studio Code"))
        {
            var parts = windowTitle.Split(" - ");
            if (parts.Length >= 2)
                return parts[^2].Trim();
        }

        // GitHub pattern: extract repo name
        if (windowTitle.Contains("GitHub"))
        {
            var githubIndex = windowTitle.IndexOf("GitHub");
            if (githubIndex > 0)
            {
                var beforeGithub = windowTitle[..githubIndex].Trim();
                var parts = beforeGithub.Split("·");
                if (parts.Length > 0)
                    return parts[0].Trim().Split("/").LastOrDefault()?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}

/// <summary>
/// A behavioral mode transition.
/// </summary>
public class BehavioralMode
{
    public string Mode { get; set; } = string.Empty;
    public string PreviousMode { get; set; } = string.Empty;
    public DateTimeOffset TransitionedAt { get; set; }
}

/// <summary>
/// App usage tracking.
/// </summary>
public class AppUsage
{
    public string ProcessName { get; set; } = string.Empty;
    public TimeSpan TotalTime { get; set; }
    public int UseCount { get; set; }
    public DateTimeOffset LastUsed { get; set; }
}

/// <summary>
/// Project activity tracking.
/// </summary>
public class ProjectActivity
{
    public string Name { get; set; } = string.Empty;
    public TimeSpan TotalTime { get; set; }
    public DateTimeOffset LastActivity { get; set; }
}

/// <summary>
/// Current environment state.
/// </summary>
public class EnvironmentState
{
    public DateTimeOffset Timestamp { get; set; }
    public string CurrentBehavioralMode { get; set; } = string.Empty;
    public string CurrentPrimaryProject { get; set; } = string.Empty;
    public List<AppSummary> TopApps { get; set; } = new();
    public List<ProjectSummary> ActiveProjects { get; set; } = new();
    public List<BehavioralMode> ModeTransitions { get; set; } = new();
}

public class AppSummary
{
    public string ProcessName { get; set; } = string.Empty;
    public TimeSpan TotalTime { get; set; }
    public int UseCount { get; set; }
}

public class ProjectSummary
{
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset LastActivity { get; set; }
    public TimeSpan TotalTime { get; set; }
}
