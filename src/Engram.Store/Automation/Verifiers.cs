using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Verifies that a specific element exists and optionally contains expected text.
/// </summary>
public class HtmlContentVerifier : IStepVerifier
{
    public string Selector { get; }
    public string? ExpectedText { get; }
    public bool CheckExistenceOnly => ExpectedText == null;

    public HtmlContentVerifier(string selector, string? expectedText = null)
    {
        Selector = selector ?? throw new ArgumentNullException(nameof(selector));
        ExpectedText = expectedText;
    }

    public async Task<bool> VerifyAsync(ExecutionContext context, CancellationToken ct)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var browser = context.GetVariable<BrowserAgentRuntime>("BrowserAgent");
        if (browser == null)
        {
            throw new InvalidOperationException("BrowserAgentRuntime is not registered in the ExecutionContext variables under 'BrowserAgent'.");
        }

        try
        {
            var text = await browser.GetTextContentAsync(Selector, ct);
            if (CheckExistenceOnly)
            {
                return true;
            }
            return text.Contains(ExpectedText!, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Verifies that the active desktop window matches a process name or window title pattern.
/// </summary>
public class ActiveWindowVerifier : IStepVerifier
{
    public string? ExpectedProcessName { get; }
    public string? ExpectedWindowTitlePattern { get; }

    public ActiveWindowVerifier(string? expectedProcessName, string? expectedWindowTitlePattern = null)
    {
        if (expectedProcessName == null && expectedWindowTitlePattern == null)
        {
            throw new ArgumentException("At least one expected process name or window title pattern must be specified.");
        }
        ExpectedProcessName = expectedProcessName;
        ExpectedWindowTitlePattern = expectedWindowTitlePattern;
    }

    public async Task<bool> VerifyAsync(ExecutionContext context, CancellationToken ct)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var op = context.GetVariable<IDesktopOperator>("DesktopOperator");
        if (op == null)
        {
            throw new InvalidOperationException("IDesktopOperator is not registered in the ExecutionContext variables under 'DesktopOperator'.");
        }

        var (process, title) = await op.GetActiveWindowAsync(ct);

        if (ExpectedProcessName != null && !process.Equals(ExpectedProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ExpectedWindowTitlePattern != null)
        {
            return Regex.IsMatch(title, ExpectedWindowTitlePattern, RegexOptions.IgnoreCase);
        }

        return true;
    }
}

/// <summary>
/// Verifies that the current browser URL matches a regex pattern or substring.
/// </summary>
public class UrlPatternVerifier : IStepVerifier
{
    public string UrlPattern { get; }

    public UrlPatternVerifier(string urlPattern)
    {
        UrlPattern = urlPattern ?? throw new ArgumentNullException(nameof(urlPattern));
    }

    public async Task<bool> VerifyAsync(ExecutionContext context, CancellationToken ct)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        string? currentUrl = null;
        var browser = context.GetVariable<BrowserAgentRuntime>("BrowserAgent");
        if (browser != null)
        {
            try
            {
                currentUrl = await browser.GetUrlAsync(ct);
            }
            catch
            {
                // Fallback to checking the context variable
            }
        }

        currentUrl ??= context.GetVariable<string>("current_url");
        if (currentUrl == null) return false;

        try
        {
            return Regex.IsMatch(currentUrl, UrlPattern, RegexOptions.IgnoreCase) || currentUrl.Contains(UrlPattern, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return currentUrl.Contains(UrlPattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>
/// Verifies that a file exists on the local filesystem using StateVerificationEngine or standard I/O.
/// </summary>
public class FileExistsVerifier : IStepVerifier
{
    public string FilePath { get; }

    public FileExistsVerifier(string filePath)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public async Task<bool> VerifyAsync(ExecutionContext context, CancellationToken ct)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var verificationEngine = context.GetVariable<StateVerificationEngine>("StateVerificationEngine");
        if (verificationEngine != null)
        {
            return await verificationEngine.VerifyFileExistsAsync(FilePath, ct);
        }

        return await Task.Run(() => System.IO.File.Exists(FilePath), ct);
    }
}

/// <summary>
/// Verifies if a specific text is visible on the screen or active window.
/// </summary>
public class OcrVerifier : IStepVerifier
{
    public string ExpectedText { get; }

    public OcrVerifier(string expectedText)
    {
        ExpectedText = expectedText ?? throw new ArgumentNullException(nameof(expectedText));
    }

    public async Task<bool> VerifyAsync(ExecutionContext context, CancellationToken ct)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var verificationEngine = context.GetVariable<StateVerificationEngine>("StateVerificationEngine");
        if (verificationEngine != null)
        {
            return await verificationEngine.VerifyOcrTextAsync(ExpectedText, ct);
        }

        return false;
    }
}

/// <summary>
/// Verifies that state changed by comparing a captured state delta.
/// </summary>
public class StateDeltaVerifier : IStepVerifier
{
    private readonly string _stateKey;
    private readonly Func<ExecutionContext, Task<object>> _captureState;
    private readonly Func<object, object, bool> _evaluateDelta;

    public StateDeltaVerifier(string stateKey, Func<ExecutionContext, Task<object>> captureState, Func<object, object, bool> evaluateDelta)
    {
        _stateKey = stateKey;
        _captureState = captureState;
        _evaluateDelta = evaluateDelta;
    }

    public async Task<bool> VerifyAsync(ExecutionContext context, CancellationToken ct)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var beforeState = context.GetVariable<object>(_stateKey + "_before");
        if (beforeState == null)
        {
            var verificationEngine = context.GetVariable<StateVerificationEngine>("StateVerificationEngine");
            if (verificationEngine != null)
            {
                return await verificationEngine.VerifyStateDeltaAsync(
                    async () => await _captureState(context),
                    _evaluateDelta, ct);
            }
            return false;
        }

        var afterState = await _captureState(context);
        return _evaluateDelta(beforeState, afterState);
    }
}
