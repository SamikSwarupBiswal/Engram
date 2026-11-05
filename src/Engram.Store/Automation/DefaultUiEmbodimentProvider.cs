using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace Engram.Store.Automation;

/// <summary>
/// Default UI embodiment provider that routes actions to BrowserAgentRuntime, IDesktopOperator,
/// or a fallback ActionExecutor based on the context variables.
/// </summary>
public class DefaultUiEmbodimentProvider : IUiEmbodimentProvider
{
    private readonly ExecutionContext _context;
    private readonly ActionExecutor _executor;
    private bool _isSimulationMode = true;

    public bool IsSimulationMode
    {
        get => _isSimulationMode;
        set
        {
            _isSimulationMode = value;
            var browserAgent = _context.GetVariable<BrowserAgentRuntime>("BrowserAgent");
            if (browserAgent != null)
            {
                browserAgent.IsSimulationMode = value;
            }
            var desktopOp = _context.GetVariable<IDesktopOperator>("DesktopOperator");
            if (desktopOp != null)
            {
                desktopOp.IsSimulationMode = value;
            }
        }
    }

    public DefaultUiEmbodimentProvider(ExecutionContext context, ActionExecutor? executor = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _executor = executor ?? new ActionExecutor();
    }

    public async Task<string> ExecuteActionAsync(AutomationAction action, CancellationToken ct = default)
    {
        if (action.AutomaticApprovalFingerprint != null && !AutomationApprovalPolicy.IsAutomatic(action))
            throw new InvalidOperationException("The action scope changed after automatic authorization. Plan the task again.");
        var requiresExplicitApproval = action.Type == ActionType.DeleteFile ||
            action.Type is ActionType.FileWrite or ActionType.MoveFile && action.RiskLevel == AutomationRiskLevel.High;
        if (requiresExplicitApproval && action.Permission != ActionPermission.Approved &&
            !(action.Permission == ActionPermission.AutoApproved && AutomationApprovalPolicy.IsAutomatic(action)))
            throw new InvalidOperationException($"{action.Type} requires explicit human approval.");

        if (action.Type is ActionType.FileWrite or ActionType.DeleteFile or ActionType.MoveFile)
            return await ExecuteFileActionAsync(action, ct);

        var browserAgent = _context.GetVariable<BrowserAgentRuntime>("BrowserAgent");
        var desktopOp = _context.GetVariable<IDesktopOperator>("DesktopOperator");

        if (browserAgent != null && IsBrowserAction(action.Type))
        {
            return await ExecuteBrowserActionAsync(browserAgent, action, ct);
        }
        else if (desktopOp != null && IsDesktopAction(action.Type))
        {
            return await ExecuteDesktopActionAsync(desktopOp, action, ct);
        }
        else
        {
            return await _executor.ExecuteAsync(action, ct);
        }
    }

    public async Task<(string ProcessName, string WindowTitle)> GetActiveWindowAsync(CancellationToken ct = default)
    {
        var desktopOp = _context.GetVariable<IDesktopOperator>("DesktopOperator");
        if (desktopOp != null)
        {
            return await desktopOp.GetActiveWindowAsync(ct);
        }
        return ("explorer", "File Explorer");
    }

    public async Task<string> GetUrlAsync(CancellationToken ct = default)
    {
        var browserAgent = _context.GetVariable<BrowserAgentRuntime>("BrowserAgent");
        if (browserAgent != null)
        {
            return await browserAgent.GetUrlAsync(ct);
        }
        return string.Empty;
    }

    private static bool IsBrowserAction(ActionType type)
    {
        return type == ActionType.Navigate || type == ActionType.Click || type == ActionType.Type || type == ActionType.Screenshot || type == ActionType.Scroll;
    }

    private static bool IsDesktopAction(ActionType type)
    {
        return type == ActionType.Click || type == ActionType.Type || type == ActionType.KeyPress;
    }

    private static async Task<string> ExecuteBrowserActionAsync(BrowserAgentRuntime browser, AutomationAction action, CancellationToken ct)
    {
        action.Status = ActionStatus.Running;
        switch (action.Type)
        {
            case ActionType.Navigate:
                var url = action.Value ?? throw new ArgumentException("Navigate requires URL");
                await browser.NavigateAsync(url, ct);
                action.Status = ActionStatus.Completed;
                return $"Navigated to {url}";
            case ActionType.Click:
                var selector = action.Target?.Selector ?? throw new ArgumentException("Click selector is required");
                await browser.ClickAsync(selector, ct);
                action.Status = ActionStatus.Completed;
                return $"Clicked selector {selector}";
            case ActionType.Type:
                var typeSelector = action.Target?.Selector ?? throw new ArgumentException("Type selector is required");
                var text = action.Value ?? throw new ArgumentException("Type value is required");
                await browser.TypeAsync(typeSelector, text, ct);
                action.Status = ActionStatus.Completed;
                return $"Typed '{text}' into {typeSelector}";
            case ActionType.Screenshot:
                var bytes = await browser.TakeScreenshotAsync(ct);
                action.Status = ActionStatus.Completed;
                return $"Screenshot captured ({bytes.Length} bytes)";
            default:
                throw new NotSupportedException($"Browser action {action.Type} is not supported in this runtime.");
        }
    }

    private static async Task<string> ExecuteDesktopActionAsync(IDesktopOperator desktop, AutomationAction action, CancellationToken ct)
    {
        action.Status = ActionStatus.Running;
        switch (action.Type)
        {
            case ActionType.Click:
                if (!action.Target?.X.HasValue ?? true || !action.Target.Y.HasValue)
                    throw new ArgumentException("Desktop Click requires X and Y coordinates");
                await desktop.ClickAsync(action.Target.X.Value, action.Target.Y.Value, ct);
                action.Status = ActionStatus.Completed;
                return $"Clicked desktop at ({action.Target.X.Value}, {action.Target.Y.Value})";
            case ActionType.Type:
                var text = action.Value ?? throw new ArgumentException("Desktop Type requires value");
                await desktop.TypeAsync(text, ct);
                action.Status = ActionStatus.Completed;
                return $"Typed desktop text '{text}'";
            case ActionType.KeyPress:
                var key = action.Value ?? throw new ArgumentException("Desktop KeyPress requires key name");
                await desktop.KeyPressAsync(key, ct);
                action.Status = ActionStatus.Completed;
                return $"Pressed desktop key '{key}'";
            default:
                throw new NotSupportedException($"Desktop action {action.Type} is not supported.");
        }
    }

    private static async Task<string> ExecuteFileActionAsync(AutomationAction action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = action.Value ?? throw new ArgumentException($"{action.Type} requires an absolute file path.");
        if (!System.IO.Path.IsPathFullyQualified(path) || path.IndexOfAny(['*', '?']) >= 0)
            throw new ArgumentException("File actions require one explicit absolute path without wildcards.");

        action.Status = ActionStatus.Running;
        if (action.Type == ActionType.FileWrite)
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
                throw new System.IO.DirectoryNotFoundException($"Target directory does not exist: {directory}");
            var content = action.Target?.Text ?? string.Empty;
            if (System.IO.File.Exists(path))
            {
                if (action.RiskLevel != AutomationRiskLevel.High || action.Permission != ActionPermission.Approved)
                    throw new InvalidOperationException("Replacing an existing local file requires explicit human approval.");
                var backupDirectory = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "Engram", "Backups", Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(backupDirectory);
                var backupPath = System.IO.Path.Combine(backupDirectory, System.IO.Path.GetFileName(path) + ".original");
                var temporaryPath = System.IO.Path.Combine(
                    directory, $".{System.IO.Path.GetFileName(path)}.engram-{Guid.NewGuid():N}.tmp");
                var replacementCommitted = false;
                try
                {
                    System.IO.File.Copy(path, backupPath, overwrite: false);
                    var originalBytes = await System.IO.File.ReadAllBytesAsync(backupPath, ct);
                    var originalHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(originalBytes));
                    await System.IO.File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), ct);
                    ct.ThrowIfCancellationRequested();
                    System.IO.File.Move(temporaryPath, path, overwrite: true);
                    replacementCommitted = true;
                    var observed = await System.IO.File.ReadAllTextAsync(path, ct);
                    if (!string.Equals(observed, content, StringComparison.Ordinal))
                        throw new InvalidOperationException("Post-overwrite exact-content verification failed.");
                    var updatedBytes = await System.IO.File.ReadAllBytesAsync(path, ct);
                    var updatedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(updatedBytes));
                    action.Status = ActionStatus.Completed;
                    return $"Replaced local file '{path}' atomically; original {originalBytes.LongLength} bytes SHA-256 {originalHash}; " +
                           $"backup '{backupPath}'; updated {updatedBytes.LongLength} bytes SHA-256 {updatedHash}; exact content verified.";
                }
                catch
                {
                    if (replacementCommitted && System.IO.File.Exists(backupPath)) System.IO.File.Copy(backupPath, path, overwrite: true);
                    if (System.IO.File.Exists(temporaryPath)) System.IO.File.Delete(temporaryPath);
                    throw;
                }
            }
            var stagedPath = System.IO.Path.Combine(directory, $".engram-{Guid.NewGuid():N}.tmp");
            try
            {
                await System.IO.File.WriteAllTextAsync(stagedPath, content, new UTF8Encoding(false), ct);
                ct.ThrowIfCancellationRequested();
                System.IO.File.Move(stagedPath, path, overwrite: false);
            }
            finally
            {
                if (System.IO.File.Exists(stagedPath)) System.IO.File.Delete(stagedPath);
            }
            action.Status = ActionStatus.Completed;
            return $"Wrote local file '{path}'";
        }

        if (action.Type == ActionType.MoveFile)
        {
            var destination = action.Target?.Text ?? throw new ArgumentException("MoveFile requires an absolute destination path in target.text.");
            if (!System.IO.Path.IsPathFullyQualified(destination) || destination.IndexOfAny(['*', '?']) >= 0)
                throw new ArgumentException("MoveFile requires one explicit absolute destination without wildcards.");
            if (!System.IO.File.Exists(path))
                throw new System.IO.FileNotFoundException("MoveFile source does not exist.", path);
            var destinationDirectory = System.IO.Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(destinationDirectory) || !System.IO.Directory.Exists(destinationDirectory))
                throw new System.IO.DirectoryNotFoundException($"Destination directory does not exist: {destinationDirectory}");
            if (System.IO.File.Exists(destination))
                throw new System.IO.IOException($"MoveFile will not overwrite the existing destination: {destination}");
            ct.ThrowIfCancellationRequested();
            System.IO.File.Move(path, destination);
            action.Status = ActionStatus.Completed;
            return $"Moved local file '{path}' to '{destination}'";
        }

        ct.ThrowIfCancellationRequested();
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        action.Status = ActionStatus.Completed;
        return $"Deleted local file '{path}'";
    }
}
