using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Stabilizes physical environmental mutations (files, clipboard, and window layouts) before verification happens.
/// </summary>
public class VerificationTemporalStabilizer
{
    private readonly RealityConvergenceTracker _convergenceTracker;
    private readonly IUiEmbodimentProvider _uiProvider;
    private readonly Func<string>? _clipboardGetter;

    public TimeSpan MaxWaitTime { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan QuietPeriod { get; set; } = TimeSpan.FromMilliseconds(500);

    public VerificationTemporalStabilizer(
        IUiEmbodimentProvider uiProvider, 
        Func<string>? clipboardGetter = null,
        RealityConvergenceTracker? convergenceTracker = null)
    {
        _uiProvider = uiProvider ?? throw new ArgumentNullException(nameof(uiProvider));
        _clipboardGetter = clipboardGetter;
        _convergenceTracker = convergenceTracker ?? new RealityConvergenceTracker();
    }

    /// <summary>
    /// Waits for the specified mutation's environment to stabilize.
    /// </summary>
    public async Task<bool> WaitForStabilizationAsync(
        MutationType type,
        string targetPathOrSelector,
        string? expectedValue,
        CancellationToken ct)
    {
        switch (type)
        {
            case MutationType.FileSaved:
            case MutationType.DownloadCompleted:
                if (string.IsNullOrWhiteSpace(targetPathOrSelector)) return false;
                
                // Track file existence, non-zero file size, and write lock release (can read successfully)
                long lastLength = -1;
                return await _convergenceTracker.TrackConvergenceAsync(async () =>
                {
                    return await Task.Run(() =>
                    {
                        if (!File.Exists(targetPathOrSelector)) return false;
                        
                        var fileInfo = new FileInfo(targetPathOrSelector);
                        long currentLength = fileInfo.Length;
                        if (currentLength <= 0 || currentLength != lastLength)
                        {
                            lastLength = currentLength;
                            return false;
                        }

                        // Check if file is readable (no active write locks)
                        try
                        {
                            using (var fs = File.OpenRead(targetPathOrSelector))
                            {
                                return true;
                            }
                        }
                        catch (IOException)
                        {
                            return false;
                        }
                    }, ct);
                }, MaxWaitTime, QuietPeriod, ct);

            case MutationType.ClipboardUpdated:
                if (expectedValue == null) return true;

                // Track clipboard content stability
                string lastClip = string.Empty;
                return await _convergenceTracker.TrackConvergenceAsync(async () =>
                {
                    var currentClip = _clipboardGetter != null ? _clipboardGetter() : string.Empty;
                    if (!currentClip.Contains(expectedValue, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    if (currentClip != lastClip)
                    {
                        lastClip = currentClip;
                        return false;
                    }
                    return true;
                }, MaxWaitTime, QuietPeriod, ct);

            case MutationType.TabOpened:
            case MutationType.EmailDrafted:
                // For browser / layout mutations, we stabilize on UI layout/rendering shifts.
                // We simulate this by checking that the active window and URL remain steady over the quiet window.
                string lastUrl = string.Empty;
                string lastWindowTitle = string.Empty;
                return await _convergenceTracker.TrackConvergenceAsync(async () =>
                {
                    var currentUrl = await _uiProvider.GetUrlAsync(ct);
                    var activeWin = await _uiProvider.GetActiveWindowAsync(ct);

                    if (currentUrl != lastUrl || activeWin.WindowTitle != lastWindowTitle)
                    {
                        lastUrl = currentUrl;
                        lastWindowTitle = activeWin.WindowTitle;
                        return false;
                    }
                    return true;
                }, MaxWaitTime, QuietPeriod, ct);

            default:
                return true;
        }
    }
}
