using System;
using System.Threading;
using System.Threading.Tasks;

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
}
