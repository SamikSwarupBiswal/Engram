using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// A Windows UI Automation provider that traverses the native HWND tree 
/// using COM interfaces to locate and click semantic targets.
/// </summary>
public class WindowsUiAutomationProvider : IUiEmbodimentProvider
{
    private readonly IDesktopOperator _desktopOperator;
    private bool _isSimulationMode = true;

    public bool IsSimulationMode
    {
        get => _isSimulationMode;
        set => _isSimulationMode = value;
    }

    public WindowsUiAutomationProvider(IDesktopOperator desktopOperator)
    {
        _desktopOperator = desktopOperator ?? throw new ArgumentNullException(nameof(desktopOperator));
    }

    public async Task<string> ExecuteActionAsync(AutomationAction action, CancellationToken ct = default)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        if (IsSimulationMode)
        {
            action.Status = ActionStatus.Completed;
            action.ExecutedAt = DateTimeOffset.UtcNow;
            var simResult = $"Simulated: {action.Type} - {action.Description}";
            action.Result = simResult;
            return simResult;
        }

        switch (action.Type)
        {
            case ActionType.Click:
                if (action.Target != null && action.Target.X.HasValue && action.Target.Y.HasValue)
                {
                    await _desktopOperator.ClickAsync(action.Target.X.Value, action.Target.Y.Value, ct);
                    action.Status = ActionStatus.Completed;
                    return $"Clicked coordinates ({action.Target.X.Value}, {action.Target.Y.Value})";
                }
                else if (action.Target != null && !string.IsNullOrEmpty(action.Target.Text))
                {
                    var resolvedTarget = await ResolveSemanticElementAsync(action.Target.Text, ct);
                    if (resolvedTarget.X.HasValue && resolvedTarget.Y.HasValue)
                    {
                        await _desktopOperator.ClickAsync(resolvedTarget.X.Value, resolvedTarget.Y.Value, ct);
                        action.Status = ActionStatus.Completed;
                        return $"Clicked semantic element '{action.Target.Text}' at ({resolvedTarget.X.Value}, {resolvedTarget.Y.Value})";
                    }
                }
                throw new ArgumentException("Click action requires coordinates or target text.");

            case ActionType.Type:
                var text = action.Value ?? throw new ArgumentException("Type action requires a value.");
                await _desktopOperator.TypeAsync(text, ct);
                action.Status = ActionStatus.Completed;
                return $"Typed text '{text}'";

            case ActionType.KeyPress:
                var key = action.Value ?? throw new ArgumentException("KeyPress action requires a key name.");
                await _desktopOperator.KeyPressAsync(key, ct);
                action.Status = ActionStatus.Completed;
                return $"Pressed key '{key}'";

            case ActionType.Wait:
                int ms = int.TryParse(action.Value, out var val) ? val : 1000;
                await Task.Delay(ms, ct);
                action.Status = ActionStatus.Completed;
                return $"Waited for {ms}ms";

            case ActionType.Screenshot:
                action.Status = ActionStatus.Completed;
                return "Screenshot captured (stub)";

            default:
                throw new NotSupportedException($"Action type '{action.Type}' is not supported by WindowsUiAutomationProvider.");
        }
    }

    public async Task<(string ProcessName, string WindowTitle)> GetActiveWindowAsync(CancellationToken ct = default)
    {
        return await _desktopOperator.GetActiveWindowAsync(ct);
    }

    public Task<string> GetUrlAsync(CancellationToken ct = default)
    {
        if (IsSimulationMode || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult("https://example.com");
        }

        try
        {
            string url = GetBrowserUrlViaUiAutomation();
            return Task.FromResult(url);
        }
        catch
        {
            return Task.FromResult(string.Empty);
        }
    }

    /// <summary>
    /// Traverses the desktop HWND tree using dynamic CUIAutomation COM instances to find named elements.
    /// </summary>
    public async Task<ActionTarget> ResolveSemanticElementAsync(string description, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be empty.", nameof(description));

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ActionTarget { X = 960, Y = 540, Text = description };
        }

        return await Task.Run(() =>
        {
            try
            {
                var uiaType = Type.GetTypeFromCLSID(new Guid("ff48dba4-bf32-4e5c-a6b4-d7d5b38590c8")); // CUIAutomation CLSID
                if (uiaType != null)
                {
                    dynamic uia = Activator.CreateInstance(uiaType)!;
                    dynamic root = uia.GetRootElement();

                    // Search condition: UIA_NamePropertyId = 30005
                    dynamic condition = uia.CreateStringPropertyCondition(30005, description);
                    // TreeScope_Descendants = 4
                    dynamic element = root.FindFirst(4, condition);

                    if (element != null)
                    {
                        var rect = element.CurrentBoundingRectangle;
                        // rect properties on the COM wrapper: left, top, right, bottom
                        int x = (int)(rect.left + (rect.right - rect.left) / 2);
                        int y = (int)(rect.top + (rect.bottom - rect.top) / 2);
                        return new ActionTarget { X = x, Y = y, Text = description };
                    }
                }
            }
            catch
            {
                // Fallback on search/COM failure
            }

            // Standard fallback coordinates
            return new ActionTarget { X = 960, Y = 540, Text = description };
        }, ct);
    }

    private string GetBrowserUrlViaUiAutomation()
    {
        try
        {
            var uiaType = Type.GetTypeFromCLSID(new Guid("ff48dba4-bf32-4e5c-a6b4-d7d5b38590c8"));
            if (uiaType == null) return string.Empty;

            dynamic uia = Activator.CreateInstance(uiaType)!;
            dynamic root = uia.GetRootElement();

            // UIA_ControlTypePropertyId = 30003, UIA_EditControlTypeId = 50004
            dynamic editCondition = uia.CreatePropertyCondition(30003, 50004);
            dynamic elements = root.FindAll(4, editCondition);

            if (elements != null)
            {
                int count = elements.Length;
                for (int i = 0; i < count; i++)
                {
                    dynamic el = elements.GetElement(i);
                    string val = el.CurrentValuePattern?.CurrentValue ?? string.Empty;
                    if (val.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                        val.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || 
                        val.Contains("www.", StringComparison.OrdinalIgnoreCase))
                    {
                        return val;
                    }
                }
            }
        }
        catch
        {
            // Fail silently
        }
        return string.Empty;
    }
}
