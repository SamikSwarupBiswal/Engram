using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Defines the abstraction boundary for executing UI actions.
/// Decouples cognitive intent and planning from platform-specific APIs and drivers.
/// </summary>
public interface IUiEmbodimentProvider
{
    /// <summary>
    /// Gets or sets whether the provider operates in simulation mode (dry-run).
    /// </summary>
    bool IsSimulationMode { get; set; }

    /// <summary>
    /// Executes a semantic automation action.
    /// </summary>
    Task<string> ExecuteActionAsync(AutomationAction action, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the current foreground window's process name and title.
    /// </summary>
    Task<(string ProcessName, string WindowTitle)> GetActiveWindowAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves the current web URL if interacting with a browser.
    /// </summary>
    Task<string> GetUrlAsync(CancellationToken ct = default);
}
