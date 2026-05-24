using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class CoexistenceMetrics
{
    public double InterruptionIrritation { get; set; } // 0 to 1
    public double AutonomyDiscomfort { get; set; } // 0 to 1
    public double PerceivedCreepiness { get; set; } // 0 to 1
    public double InterventionUsefulness { get; set; } // 0 to 1
    public double SilenceQuality { get; set; } // 0 to 1
    public double TransparencyClarity { get; set; } // 0 to 1
    public double ApprovalFatigue { get; set; } // 0 to 1
    public double CognitiveResidue { get; set; } // 0 to 1
}

public class CoexistenceMetricsTracker
{
    private readonly string _telemetryDir;
    private readonly object _lock = new();

    // In-memory accumulation
    private int _dismissalsCount = 0;
    private int _cancellationsCount = 0;
    private int _successfulActionsCount = 0;
    private int _failedActionsCount = 0;
    private int _backgroundActionsCount = 0;
    private int _foregroundActionsCount = 0;
    private int _userActiveActionsCount = 0;
    private double _totalSilenceDurationSeconds = 0;
    private int _acceptedInterventions = 0;
    private int _dismissedInterventions = 0;
    private int _approvalPromptsCount = 0;

    // --- Phase D6: Dogfooding Metrics ---
    private int _manualOverrides = 0;
    private int _abortedWorkflows = 0;
    private int _ignoredInterventions = 0;
    private int _resumedWorkflows = 0;
    private int _userFrustrationMoments = 0;
    private int _recoveryEvents = 0;
    private int _startedWorkflows = 0;
    private int _completedWorkflows = 0;

    public CoexistenceMetricsTracker(string? customBaseDir = null)
    {
        var baseDir = customBaseDir ?? Path.Combine(Environment.CurrentDirectory, ".engram");
        _telemetryDir = Path.Combine(baseDir, "telemetry");
        Directory.CreateDirectory(_telemetryDir);
    }

    public void RecordInterruption(bool isCancel)
    {
        lock (_lock)
        {
            if (isCancel) _cancellationsCount++;
            else _dismissalsCount++;
        }
    }

    public void RecordSilence(double durationSeconds)
    {
        lock (_lock)
        {
            _totalSilenceDurationSeconds += durationSeconds;
        }
    }

    public void RecordAction(bool isBackground, bool userActive, bool succeeded)
    {
        lock (_lock)
        {
            if (succeeded) _successfulActionsCount++;
            else _failedActionsCount++;

            if (isBackground) _backgroundActionsCount++;
            else _foregroundActionsCount++;

            if (userActive) _userActiveActionsCount++;
        }
    }

    public void RecordIntervention(bool accepted)
    {
        lock (_lock)
        {
            if (accepted) _acceptedInterventions++;
            else _dismissedInterventions++;
        }
    }

    public void RecordApprovalPrompt()
    {
        lock (_lock)
        {
            _approvalPromptsCount++;
        }
    }

    public void RecordManualOverride()
    {
        lock (_lock)
        {
            _manualOverrides++;
            _userFrustrationMoments++;
        }
    }

    public void RecordAbortedWorkflow()
    {
        lock (_lock)
        {
            _abortedWorkflows++;
        }
    }

    public void RecordIgnoredIntervention()
    {
        lock (_lock)
        {
            _ignoredInterventions++;
        }
    }

    public void RecordResumedWorkflow()
    {
        lock (_lock)
        {
            _resumedWorkflows++;
        }
    }

    public void RecordUserFrustrationMoment()
    {
        lock (_lock)
        {
            _userFrustrationMoments++;
        }
    }

    public void RecordRecoveryEvent()
    {
        lock (_lock)
        {
            _recoveryEvents++;
        }
    }

    public void RecordWorkflowActivity(bool started, bool completed)
    {
        lock (_lock)
        {
            if (started) _startedWorkflows++;
            if (completed) _completedWorkflows++;
        }
    }

    public CoexistenceMetrics CalculateMetrics()
    {
        lock (_lock)
        {
            double irritation = 0.0;
            double totalInteractions = _successfulActionsCount + _failedActionsCount;
            if (totalInteractions > 0)
            {
                irritation = (_dismissalsCount + _cancellationsCount * 2.0) / (totalInteractions + 1.0);
            }
            irritation = Math.Min(1.0, irritation);

            double discomfort = 0.0;
            if (totalInteractions > 0)
            {
                discomfort = (double)_userActiveActionsCount / totalInteractions;
            }

            double creepiness = 0.0;
            if (totalInteractions > 0)
            {
                // High frequency of foreground actions when user is active is creepy
                creepiness = ((double)_foregroundActionsCount * _userActiveActionsCount) / ((totalInteractions * totalInteractions) + 1.0);
            }
            creepiness = Math.Min(1.0, creepiness);

            double usefulness = 1.0;
            double totalInterventions = _acceptedInterventions + _dismissedInterventions;
            if (totalInterventions > 0)
            {
                usefulness = (double)_acceptedInterventions / totalInterventions;
            }

            double silenceQuality = 1.0;
            if (_totalSilenceDurationSeconds > 0)
            {
                // Silence quality is higher if there are fewer interruptions per hour of silence
                double interruptions = _dismissalsCount + _cancellationsCount + _approvalPromptsCount;
                double hours = _totalSilenceDurationSeconds / 3600.0;
                silenceQuality = 1.0 / (1.0 + (interruptions / (hours + 0.1)));
            }

            double clarity = 0.95; // default high transparency

            double fatigue = 0.0;
            if (totalInteractions > 0)
            {
                fatigue = (double)_approvalPromptsCount / (totalInteractions + 1.0);
            }
            fatigue = Math.Min(1.0, fatigue);

            // Calculate Cognitive Residue (0 to 1 scale)
            double dismissalRate = totalInterventions > 0 ? (double)_dismissedInterventions / totalInterventions : 0.0;
            double overrideBurden = Math.Min(1.0, _manualOverrides * 0.15 + _userFrustrationMoments * 0.1);
            double abortBurden = Math.Min(1.0, _abortedWorkflows * 0.25);
            
            double cognitiveResidue = (dismissalRate * 0.3) + (overrideBurden * 0.4) + (abortBurden * 0.3);
            cognitiveResidue = Math.Min(1.0, Math.Max(0.0, cognitiveResidue));

            return new CoexistenceMetrics
            {
                InterruptionIrritation = irritation,
                AutonomyDiscomfort = discomfort,
                PerceivedCreepiness = creepiness,
                InterventionUsefulness = usefulness,
                SilenceQuality = silenceQuality,
                TransparencyClarity = clarity,
                ApprovalFatigue = fatigue,
                CognitiveResidue = cognitiveResidue
            };
        }
    }

    public string GenerateCoexistenceReport()
    {
        var metrics = CalculateMetrics();
        var report = new
        {
            ReportType = "Coexistence Health Report",
            Timestamp = DateTimeOffset.UtcNow,
            Metrics = metrics,
            Interpretation = new
            {
                IrritationStatus = metrics.InterruptionIrritation < 0.3 ? "Calm" : metrics.InterruptionIrritation < 0.6 ? "Frustrated" : "Exhausting",
                AutonomyTrustStatus = metrics.AutonomyDiscomfort < 0.3 ? "Comfortable Pacing" : "Aggressive Intrusion",
                CoexistenceAcceptance = metrics.PerceivedCreepiness < 0.25 ? "Highly Respectful" : "Intrusive/Creepy"
            }
        };

        var path = Path.Combine(_telemetryDir, "coexistence_report.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public string GenerateExecutionReliabilityReport()
    {
        lock (_lock)
        {
            var total = _successfulActionsCount + _failedActionsCount;
            var successRate = total > 0 ? (double)_successfulActionsCount / total : 1.0;

            var report = new
            {
                ReportType = "Execution Reliability Report",
                Timestamp = DateTimeOffset.UtcNow,
                TotalActionsExecuted = total,
                SuccessfulActions = _successfulActionsCount,
                FailedActions = _failedActionsCount,
                ActionSuccessRate = successRate,
                ReliabilityStatus = successRate >= 0.95 ? "High Reliability" : successRate >= 0.85 ? "Stable" : "Degraded"
            };

            var path = Path.Combine(_telemetryDir, "execution_reliability_report.json");
            File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }
    }

    public string GenerateTrustPacingReport()
    {
        var metrics = CalculateMetrics();
        var report = new
        {
            ReportType = "Trust Pacing Report",
            Timestamp = DateTimeOffset.UtcNow,
            AutonomyDiscomfort = metrics.AutonomyDiscomfort,
            ApprovalFatigue = metrics.ApprovalFatigue,
            PacingCalibration = new
            {
                RecommendedActionDelayMs = metrics.ApprovalFatigue > 0.5 ? 5000 : 1000,
                RequiresGateLockout = metrics.InterruptionIrritation > 0.7
            }
        };

        var path = Path.Combine(_telemetryDir, "trust_pacing_report.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public string GenerateEcologicalHealthReport()
    {
        var metrics = CalculateMetrics();
        var report = new
        {
            ReportType = "Ecological Health Report",
            Timestamp = DateTimeOffset.UtcNow,
            SilenceQuality = metrics.SilenceQuality,
            MetabolicCoexistenceScore = (metrics.SilenceQuality * 0.4) + (metrics.InterventionUsefulness * 0.3) + ((1.0 - metrics.InterruptionIrritation) * 0.3),
            EcologyStatus = metrics.SilenceQuality > 0.8 ? "Healthy Equilibrium" : "Stressed/Entropy Accumulating"
        };

        var path = Path.Combine(_telemetryDir, "ecological_health_report.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public string GenerateFailureArchaeologyReport(List<FailureRecord> failures)
    {
        var groupedFailures = failures.GroupBy(f => f.ErrorType)
                                      .ToDictionary(g => g.Key.ToString(), g => g.Count());

        var report = new
        {
            ReportType = "Failure Archaeology Analysis",
            Timestamp = DateTimeOffset.UtcNow,
            TotalFailuresRecorded = failures.Count,
            FailuresByType = groupedFailures,
            RecoverySuccessRate = failures.Count > 0 ? (double)failures.Count(f => f.RecoverySucceeded) / failures.Count : 1.0
        };

        var path = Path.Combine(_telemetryDir, "failure_archaeology_report.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public string GenerateInterventionFatigueReport()
    {
        var metrics = CalculateMetrics();
        var report = new
        {
            ReportType = "Intervention Fatigue Evaluation",
            Timestamp = DateTimeOffset.UtcNow,
            InterventionDismissalRate = _dismissedInterventions / (double)(_acceptedInterventions + _dismissedInterventions + 1),
            FatigueSeverity = metrics.ApprovalFatigue > 0.6 ? "Critical Fatigue" : metrics.ApprovalFatigue > 0.3 ? "Moderate Fatigue" : "Calm",
            ShouldRestrictPace = metrics.ApprovalFatigue > 0.4 || metrics.InterruptionIrritation > 0.5
        };

        var path = Path.Combine(_telemetryDir, "intervention_fatigue_report.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}
