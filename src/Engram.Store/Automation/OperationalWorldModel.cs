using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Engram.Store.Events;

namespace Engram.Store.Automation;

/// <summary>
/// Operational World Model tracks Engram's live understanding of the machine state
/// during execution.
/// </summary>
public class OperationalWorldModel
{
    private readonly IEventBus _eventBus;
    private readonly object _lock = new();

    private string _activeWorkflow = string.Empty;
    private string _currentPhase = string.Empty;
    private int _browserTabsCount;
    private string _activeDocument = string.Empty;
    private double _executionConfidence = 1.0;
    private int _interruptionCount;
    private TimeSpan? _estimatedCompletion;
    private readonly ConcurrentDictionary<string, string> _environmentalConstraints = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _executionTrajectory = new();

    public OperationalWorldModel(IEventBus eventBus)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public string ActiveWorkflow
    {
        get { lock (_lock) return _activeWorkflow; }
        set
        {
            lock (_lock)
            {
                if (_activeWorkflow != value)
                {
                    _activeWorkflow = value;
                    PublishStateChanged("ActiveWorkflow", value);
                }
            }
        }
    }

    public string CurrentPhase
    {
        get { lock (_lock) return _currentPhase; }
        set
        {
            lock (_lock)
            {
                if (_currentPhase != value)
                {
                    _currentPhase = value;
                    PublishStateChanged("CurrentPhase", value);
                }
            }
        }
    }

    public int BrowserTabsCount
    {
        get { lock (_lock) return _browserTabsCount; }
        set
        {
            lock (_lock)
            {
                if (_browserTabsCount != value)
                {
                    _browserTabsCount = value;
                    PublishStateChanged("BrowserTabsCount", value);
                }
            }
        }
    }

    public string ActiveDocument
    {
        get { lock (_lock) return _activeDocument; }
        set
        {
            lock (_lock)
            {
                if (_activeDocument != value)
                {
                    _activeDocument = value;
                    PublishStateChanged("ActiveDocument", value);
                }
            }
        }
    }

    public double ExecutionConfidence
    {
        get { lock (_lock) return _executionConfidence; }
        set
        {
            lock (_lock)
            {
                if (Math.Abs(_executionConfidence - value) > 0.0001)
                {
                    _executionConfidence = value;
                    PublishStateChanged("ExecutionConfidence", value);
                }
            }
        }
    }

    public int InterruptionCount
    {
        get { lock (_lock) return _interruptionCount; }
        set
        {
            lock (_lock)
            {
                if (_interruptionCount != value)
                {
                    _interruptionCount = value;
                    PublishStateChanged("InterruptionCount", value);
                }
            }
        }
    }

    public TimeSpan? EstimatedCompletion
    {
        get { lock (_lock) return _estimatedCompletion; }
        set
        {
            lock (_lock)
            {
                if (_estimatedCompletion != value)
                {
                    _estimatedCompletion = value;
                    PublishStateChanged("EstimatedCompletion", value);
                }
            }
        }
    }

    public IDictionary<string, string> EnvironmentalConstraints => _environmentalConstraints;

    public void SetEnvironmentalConstraint(string key, string value)
    {
        _environmentalConstraints[key] = value;
        PublishStateChanged($"Constraint:{key}", value);
    }

    public void RemoveEnvironmentalConstraint(string key)
    {
        if (_environmentalConstraints.TryRemove(key, out _))
        {
            PublishStateChanged($"ConstraintRemoved:{key}", string.Empty);
        }
    }

    public IReadOnlyList<string> ExecutionTrajectory
    {
        get
        {
            lock (_lock)
            {
                return _executionTrajectory.ToArray();
            }
        }
    }

    public void AddTrajectoryMilestone(string milestone)
    {
        lock (_lock)
        {
            _executionTrajectory.Add(milestone);
            PublishStateChanged("TrajectoryAdded", milestone);
        }
    }

    public void ClearTrajectory()
    {
        lock (_lock)
        {
            _executionTrajectory.Clear();
            PublishStateChanged("TrajectoryCleared", string.Empty);
        }
    }

    public void Update(Action<OperationalWorldModel> updateAction)
    {
        lock (_lock)
        {
            updateAction(this);
        }
        PublishStateChanged("BatchUpdate", "Multiple changes applied");
    }

    public void UpdateState(
        string currentPhase,
        string activeWorkflow,
        string activeDocument,
        int browserTabsCount,
        IDictionary<string, string> environmentalConstraints)
    {
        Update(m =>
        {
            m.CurrentPhase = currentPhase;
            m.ActiveWorkflow = activeWorkflow;
            m.ActiveDocument = activeDocument;
            m.BrowserTabsCount = browserTabsCount;
            
            // Safe clear and copy constraints
            var keys = new List<string>(m.EnvironmentalConstraints.Keys);
            foreach (var key in keys)
            {
                m.RemoveEnvironmentalConstraint(key);
            }
            if (environmentalConstraints != null)
            {
                foreach (var kvp in environmentalConstraints)
                {
                    m.SetEnvironmentalConstraint(kvp.Key, kvp.Value);
                }
            }
        });
    }

    public object GetSnapshot()
    {
        lock (_lock)
        {
            return new
            {
                ActiveWorkflow = _activeWorkflow,
                CurrentPhase = _currentPhase,
                BrowserTabsCount = _browserTabsCount,
                ActiveDocument = _activeDocument,
                ExecutionConfidence = _executionConfidence,
                InterruptionCount = _interruptionCount,
                EstimatedCompletion = _estimatedCompletion?.ToString(),
                EnvironmentalConstraints = new Dictionary<string, string>(_environmentalConstraints),
                ExecutionTrajectory = new List<string>(_executionTrajectory)
            };
        }
    }

    private void PublishStateChanged(string propertyName, object value)
    {
        _eventBus.Publish(new EventEnvelope
        {
            EventType = "automation.worldmodel.changed",
            Source = "operational_world_model",
            Payload = new
            {
                Property = propertyName,
                Value = value,
                Timestamp = DateTimeOffset.UtcNow
            }
        });
    }
}
