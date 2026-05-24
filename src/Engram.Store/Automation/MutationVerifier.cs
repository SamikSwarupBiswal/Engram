using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows; // For clipboard verification if needed, or using custom clipboard provider

namespace Engram.Store.Automation;

public enum MutationType
{
    FileSaved,
    EmailDrafted,
    TabOpened,
    DownloadCompleted,
    ClipboardUpdated
}

public class MutationVerifier
{
    private readonly IUiEmbodimentProvider _uiProvider;
    private readonly Func<string>? _clipboardGetter;

    public MutationVerifier(IUiEmbodimentProvider uiProvider, Func<string>? clipboardGetter = null)
    {
        _uiProvider = uiProvider ?? throw new ArgumentNullException(nameof(uiProvider));
        _clipboardGetter = clipboardGetter;
    }

    public async Task<bool> VerifyMutationAsync(MutationType type, string targetPathOrSelector, string? expectedValue = null, CancellationToken ct = default)
    {
        switch (type)
        {
            case MutationType.FileSaved:
                if (string.IsNullOrWhiteSpace(targetPathOrSelector)) return false;
                return await Task.Run(() => 
                {
                    if (!File.Exists(targetPathOrSelector)) return false;
                    if (expectedValue != null)
                    {
                        var content = File.ReadAllText(targetPathOrSelector);
                        return content.Contains(expectedValue, StringComparison.OrdinalIgnoreCase);
                    }
                    return true;
                }, ct);

            case MutationType.TabOpened:
                if (string.IsNullOrWhiteSpace(targetPathOrSelector)) return false;
                var currentUrl = await _uiProvider.GetUrlAsync(ct);
                return currentUrl.Contains(targetPathOrSelector, StringComparison.OrdinalIgnoreCase);

            case MutationType.ClipboardUpdated:
                if (expectedValue == null) return true;
                var clipText = _clipboardGetter != null ? _clipboardGetter() : string.Empty;
                return clipText.Contains(expectedValue, StringComparison.OrdinalIgnoreCase);

            case MutationType.DownloadCompleted:
                if (string.IsNullOrWhiteSpace(targetPathOrSelector)) return false;
                return await Task.Run(() => 
                {
                    // Check if file exists and has size > 0
                    if (!File.Exists(targetPathOrSelector)) return false;
                    var info = new FileInfo(targetPathOrSelector);
                    return info.Length > 0;
                }, ct);

            case MutationType.EmailDrafted:
                // Email draft is verified via DOM selector existence or expected value in browser/app
                if (string.IsNullOrWhiteSpace(targetPathOrSelector)) return false;
                var action = new AutomationAction
                {
                    Type = ActionType.Wait,
                    Target = new ActionTarget { Selector = targetPathOrSelector },
                    Value = "0"
                };
                var result = await _uiProvider.ExecuteActionAsync(action, ct);
                if (action.Status != ActionStatus.Completed || result.Contains("fail", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (expectedValue != null)
                {
                    return result.Contains(expectedValue, StringComparison.OrdinalIgnoreCase);
                }
                return true;

            default:
                return false;
        }
    }
}
