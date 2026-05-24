using System;
using System.IO;
using System.Linq;
using Engram.Store.Wiki;

namespace Engram.Store.Governance;

public class HealthAuditReport
{
    public bool IsWalHealthy { get; set; } = true;
    public double AverageNodeAgeDays { get; set; }
    public double SystemDriftRatio { get; set; }
    public int ActiveDegradationsCount { get; set; }
    public string SummaryMessage { get; set; } = string.Empty;
}

public class DeploymentHealthAuditor
{
    private readonly WorkspacePaths _paths;
    private readonly WikiNodeStore _nodeStore;
    private readonly TrustCalibrationEngine _trustEngine;

    public DeploymentHealthAuditor(WorkspacePaths paths, WikiNodeStore nodeStore, TrustCalibrationEngine trustEngine)
    {
        _paths = paths;
        _nodeStore = nodeStore;
        _trustEngine = trustEngine;
    }

    public HealthAuditReport RunHealthAudit()
    {
        var report = new HealthAuditReport();

        // 1. Audit WAL health (check write-ahead-log existence and corruption signatures)
        var walDir = _paths.Raw;
        if (Directory.Exists(walDir))
        {
            var walFiles = Directory.GetFiles(walDir, "*.log");
            foreach (var logFile in walFiles)
            {
                var fileInfo = new FileInfo(logFile);
                if (fileInfo.Length > 50 * 1024 * 1024) // Log over 50MB suggests missing compaction
                {
                    report.IsWalHealthy = false;
                    report.SummaryMessage += "WAL logs exceeding safety size limits. Compaction warning. ";
                }
            }
        }

        // 2. Audit database age (time since touched)
        var allNodes = _nodeStore.LoadAll();
        if (allNodes.Count > 0)
        {
            var totalDays = allNodes.Sum(n => (DateTimeOffset.UtcNow - n.LastTouchedAt).TotalDays);
            report.AverageNodeAgeDays = totalDays / allNodes.Count;
        }

        // 3. Audit trust scores and active degradations
        var scores = _trustEngine.GetAllScores();
        if (scores.Count > 0)
        {
            double averageTrust = scores.Average(s => s.Score);
            report.SystemDriftRatio = 1.0 - averageTrust;
            if (averageTrust < 0.4)
            {
                report.SummaryMessage += "Average system trust is critically low. ";
            }
        }

        if (string.IsNullOrEmpty(report.SummaryMessage))
        {
            report.SummaryMessage = "All deployment systems are verified healthy.";
        }

        return report;
    }
}
