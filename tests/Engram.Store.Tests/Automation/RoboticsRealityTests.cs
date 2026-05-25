using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Engram.Store.Automation;

namespace Engram.Store.Tests.Automation;

public class RoboticsRealityTests : IDisposable
{
    private readonly string _tempDir;

    public RoboticsRealityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task TemporalRealityConvergence_ShouldSucceed_WhenConditionResolvesWithinWindow()
    {
        // Arrange
        var tracker = new RealityConvergenceTracker(TimeSpan.FromMilliseconds(10));
        int callCount = 0;
        Func<Task<bool>> checkCondition = async () =>
        {
            callCount++;
            // Stable true state starting from 3rd call
            return await Task.FromResult(callCount >= 3);
        };

        // Act
        var result = await tracker.TrackConvergenceAsync(
            checkCondition,
            totalTimeout: TimeSpan.FromMilliseconds(500),
            quietWindow: TimeSpan.FromMilliseconds(50),
            ct: CancellationToken.None
        );

        // Assert
        Assert.True(result);
        Assert.True(callCount >= 3);
    }

    [Fact]
    public async Task TemporalRealityConvergence_ShouldFail_WhenConditionRemainsInconsistent()
    {
        // Arrange
        var tracker = new RealityConvergenceTracker(TimeSpan.FromMilliseconds(10));
        int callCount = 0;
        Func<Task<bool>> checkCondition = async () =>
        {
            callCount++;
            // Alternates state, never settles
            return await Task.FromResult(callCount % 2 == 0);
        };

        // Act
        var result = await tracker.TrackConvergenceAsync(
            checkCondition,
            totalTimeout: TimeSpan.FromMilliseconds(100),
            quietWindow: TimeSpan.FromMilliseconds(40),
            ct: CancellationToken.None
        );

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task VerificationTemporalStabilizer_ShouldStabilizeFile()
    {
        // Arrange
        var mockUi = new MockUiProvider();
        var tracker = new RealityConvergenceTracker(TimeSpan.FromMilliseconds(10));
        var stabilizer = new VerificationTemporalStabilizer(mockUi, null, tracker)
        {
            MaxWaitTime = TimeSpan.FromMilliseconds(300),
            QuietPeriod = TimeSpan.FromMilliseconds(50)
        };

        var filePath = Path.Combine(_tempDir, "test.txt");

        // Act: Start a background task writing to the file after 50ms
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            File.WriteAllText(filePath, "ready");
        });

        var result = await stabilizer.WaitForStabilizationAsync(
            MutationType.FileSaved,
            filePath,
            expectedValue: null,
            ct: CancellationToken.None
        );

        // Assert
        Assert.True(result);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task OverlaySafetyRules_ShouldForbidForbiddenModals()
    {
        // Arrange
        var interruptGraph = new EnvironmentalInterruptGraph();
        var context = new Engram.Store.Automation.ExecutionContext();

        // Act: Assess a UAC or delete confirmation prompt
        var resultProcess = await interruptGraph.AssessAndHandleInterruptAsync(
            "consent", "UAC prompt", context, CancellationToken.None);
            
        var resultTitle = await interruptGraph.AssessAndHandleInterruptAsync(
            "explorer", "Confirm file deletion", context, CancellationToken.None);

        // Assert
        Assert.False(resultProcess); // requires human
        Assert.False(resultTitle);   // requires human
    }

    [Fact]
    public async Task OverlaySafetyRules_ShouldDefaultUnknownModalsToSuspend()
    {
        // Arrange
        var interruptGraph = new EnvironmentalInterruptGraph();
        var context = new Engram.Store.Automation.ExecutionContext();

        // Act: Assess an unknown window title
        var result = await interruptGraph.AssessAndHandleInterruptAsync(
            "someapp", "Some completely random popup title", context, CancellationToken.None);

        // Assert
        Assert.False(result); // Must default to Suspend and yield control
    }

    [Fact]
    public async Task OverlaySafetyRules_ShouldAllowAutoDismissableOrSafeRegisteredModals()
    {
        // Arrange
        var interruptGraph = new EnvironmentalInterruptGraph();
        var context = new Engram.Store.Automation.ExecutionContext();

        // Register custom safe handler
        interruptGraph.RegisterSafeInterruptHandler("Optional Tips Window", (ctx, ct) => Task.FromResult(true));

        // Act
        var resultInfo = await interruptGraph.AssessAndHandleInterruptAsync(
            "app", "Save Success Notification", context, CancellationToken.None);
            
        var resultCustom = await interruptGraph.AssessAndHandleInterruptAsync(
            "app", "Optional Tips Window", context, CancellationToken.None);

        // Assert
        Assert.True(resultInfo);   // Notification/Tip matches AutoDismissable
        Assert.True(resultCustom); // Matches custom registered safe handler
    }

    [Fact]
    public async Task ProceduralExperience_ShouldRecordAndDecayDelayMetrics()
    {
        // Arrange
        var store = new ProceduralExperienceStore(_tempDir, halfLifeDays: 0.0000001); // ultra short half life for testing decay (approx 8.6ms)
        
        // Act: Record success metrics
        store.RecordMetric("TestApp", "1.0", ActionType.Click, "button#submit", TimeSpan.FromMilliseconds(200), success: true);
        store.RecordMetric("TestApp", "1.0", ActionType.Click, "button#submit", TimeSpan.FromMilliseconds(400), success: true);

        // Assert average calculation
        var entry = store.GetEntry("TestApp", "1.0", ActionType.Click, "button#submit");
        Assert.NotNull(entry);
        Assert.Equal(300.0, entry.AverageDurationMs);
        Assert.Equal(2, entry.SuccessCount);

        // Get recommended delay (average * 1.5 = 450)
        var recommended = store.GetRecommendedDelay("TestApp", "1.0", ActionType.Click, "button#submit");
        Assert.Equal(450, recommended.TotalMilliseconds);

        // Decay confidence: wait a bit to exceed the short half-life
        await Task.Delay(50);
        var confidence = store.GetConfidence("TestApp", "1.0", ActionType.Click, "button#submit");
        Assert.True(confidence < 0.2);

        // Recommended delay should revert to default Click delay (500ms) after confidence decay
        var recommendedDecayed = store.GetRecommendedDelay("TestApp", "1.0", ActionType.Click, "button#submit");
        Assert.Equal(500, recommendedDecayed.TotalMilliseconds);
    }

    [Fact]
    public async Task ProceduralDriftDetector_ShouldFlagAnomalousSpikes()
    {
        // Arrange
        var store = new ProceduralExperienceStore(_tempDir, halfLifeDays: 7.0);
        var detector = new ProceduralDriftDetector(store);

        // Record a set of stable durations with non-zero standard deviation
        store.RecordMetric("TestApp", "1.0", ActionType.Click, "btn", TimeSpan.FromMilliseconds(95), success: true);
        store.RecordMetric("TestApp", "1.0", ActionType.Click, "btn", TimeSpan.FromMilliseconds(105), success: true);
        store.RecordMetric("TestApp", "1.0", ActionType.Click, "btn", TimeSpan.FromMilliseconds(100), success: true);
        store.RecordMetric("TestApp", "1.0", ActionType.Click, "btn", TimeSpan.FromMilliseconds(98), success: true);
        store.RecordMetric("TestApp", "1.0", ActionType.Click, "btn", TimeSpan.FromMilliseconds(102), success: true);

        // Assert entry has small standard deviation
        var entry = store.GetEntry("TestApp", "1.0", ActionType.Click, "btn");
        Assert.NotNull(entry);
        Assert.True(entry.StandardDeviationMs >= 0);

        // Act: Test an action duration that is a huge spike (1000ms is > 3 std devs + 200ms threshold)
        var drift = detector.DetectDrift("TestApp", "1.0", ActionType.Click, "btn", TimeSpan.FromMilliseconds(1000), success: true, activeWindowTitle: null, out var reason);

        // Assert
        Assert.NotNull(drift);
        Assert.Equal(UncertaintyLevel.U1_Observational, drift);
        Assert.Contains("exceeds historical average", reason);
    }

    [Fact]
    public async Task ProceduralDriftDetector_ShouldFlagConsecutiveFailuresOfSafeSelector()
    {
        // Arrange
        var store = new ProceduralExperienceStore(_tempDir, halfLifeDays: 7.0);
        var detector = new ProceduralDriftDetector(store);

        // Record high success rate historically with enough samples to keep success rate > 90% after 2 failures
        for (int i = 0; i < 25; i++)
        {
            store.RecordMetric("TestApp", "1.0", ActionType.Click, "btn", TimeSpan.FromMilliseconds(100), success: true);
        }

        // Simulate 2 failures consecutively
        store.RecordMetric("TestApp", "1.0", ActionType.Click, "btn", TimeSpan.FromMilliseconds(100), success: false);
        store.RecordMetric("TestApp", "1.0", ActionType.Click, "btn", TimeSpan.FromMilliseconds(100), success: false);

        // Act
        var drift = detector.DetectDrift("TestApp", "1.0", ActionType.Click, "btn", TimeSpan.FromMilliseconds(100), success: false, activeWindowTitle: null, out var reason);

        // Assert
        Assert.NotNull(drift);
        Assert.Equal(UncertaintyLevel.U2_StateAmbiguity, drift);
        Assert.Contains("failed 2 times consecutively", reason);
    }
}
