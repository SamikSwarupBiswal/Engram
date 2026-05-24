using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Automation;

namespace Engram.Store.Tests.Automation;

public class VerificationConsensusTests
{
    [Fact]
    public void VerificationConsensusEngine_ShouldApplyEpistemicConservatism()
    {
        var engine = new VerificationConsensusEngine();

        // 1. Strong verified signals (API + DOM)
        var signals1 = new VerificationSignals
        {
            StructuredApiVerified = true,
            DomVerified = true
        };
        double conf1 = engine.CalculateRealityConfidence(signals1);
        Assert.True(conf1 > 0.8);

        // 2. Failure penalty: one checked signal failed
        var signals2 = new VerificationSignals
        {
            StructuredApiVerified = true,
            DomVerified = false
        };
        double conf2 = engine.CalculateRealityConfidence(signals2);
        Assert.True(conf2 < conf1 * 0.6); // Penalized heavily

        // 3. Weak signal cap: only OCR/visual checked
        var signals3 = new VerificationSignals
        {
            OcrVerified = true,
            HeuristicVisualVerified = true
        };
        double conf3 = engine.CalculateRealityConfidence(signals3);
        Assert.True(conf3 < 0.7); // Capped below 0.7 due to weak checks only
    }

    [Fact]
    public void VerificationStrengthPolicy_ShouldEnforceRiskScales()
    {
        var policy = new VerificationStrengthPolicy();
        var signals = new VerificationSignals
        {
            StructuredApiVerified = true
        };

        // Low risk accepts low certainty
        Assert.True(policy.MeetsVerificationRequirements(RiskLevel.LowRisk, 0.4, signals));

        // High risk demands high certainty and strong signals
        Assert.False(policy.MeetsVerificationRequirements(RiskLevel.HighRisk, 0.4, signals));
        Assert.True(policy.MeetsVerificationRequirements(RiskLevel.HighRisk, 0.85, signals));

        // Extremely high demands dual strong signals
        Assert.False(policy.MeetsVerificationRequirements(RiskLevel.ExtremelyHigh, 0.95, signals));

        var dualSignals = new VerificationSignals
        {
            StructuredApiVerified = true,
            AccessibilityVerified = true
        };
        Assert.True(policy.MeetsVerificationRequirements(RiskLevel.ExtremelyHigh, 0.95, dualSignals));
    }

    [Fact]
    public async Task MutationVerifier_ShouldCheckFileSavesAndClipboard()
    {
        var uiMock = new MockUiProvider();
        var clipboardText = "Verified Clip Content";
        var verifier = new MutationVerifier(uiMock, () => clipboardText);

        // Clipboard
        Assert.True(await verifier.VerifyMutationAsync(MutationType.ClipboardUpdated, "", "Verified Clip"));
        Assert.False(await verifier.VerifyMutationAsync(MutationType.ClipboardUpdated, "", "Different Text"));

        // Filesystem
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Saved File Content");
            Assert.True(await verifier.VerifyMutationAsync(MutationType.FileSaved, tempFile, "Content"));
            Assert.False(await verifier.VerifyMutationAsync(MutationType.FileSaved, tempFile, "Missing"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task FalseCompletionDetector_ShouldDetectErrorsAndDialogs()
    {
        var uiMock = new MockUiProvider();
        var detector = new FalseCompletionDetector(uiMock);

        // Normal screen
        uiMock.MockProcessName = "chrome.exe";
        uiMock.MockWindowTitle = "Google Search";
        Assert.False(await detector.DetectFalseCompletionAsync("Success result"));

        // Error Dialog Active
        uiMock.MockWindowTitle = "Error - Operation Failed";
        Assert.True(await detector.DetectFalseCompletionAsync("Success result"));

        // Failure in snippet
        uiMock.MockWindowTitle = "Google Search";
        Assert.True(await detector.DetectFalseCompletionAsync("Internal server error: 500"));
    }
}
