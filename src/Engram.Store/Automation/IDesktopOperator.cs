using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Interface for executing and simulating desktop operating system actions.
/// </summary>
public interface IDesktopOperator
{
    /// <summary>
    /// Gets or sets whether the operator runs in simulation mode (dry-run).
    /// </summary>
    bool IsSimulationMode { get; set; }

    /// <summary>
    /// Simulates or executes a mouse click at coordinates (x, y).
    /// </summary>
    Task ClickAsync(int x, int y, CancellationToken ct = default);

    /// <summary>
    /// Simulates or executes typing text.
    /// </summary>
    Task TypeAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Simulates or executes a special key press (e.g. Enter, Escape, Backspace).
    /// </summary>
    Task KeyPressAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the current foreground window's process name and title.
    /// </summary>
    Task<(string ProcessName, string WindowTitle)> GetActiveWindowAsync(CancellationToken ct = default);
}
