using System;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Resolves high-level semantic descriptions (e.g. "Save button", "Search field")
/// to specific actionable targets (coordinates or selectors) using the active provider.
/// </summary>
public class SemanticElementResolver
{
    private readonly IUiEmbodimentProvider _uiProvider;

    public SemanticElementResolver(IUiEmbodimentProvider uiProvider)
    {
        _uiProvider = uiProvider ?? throw new ArgumentNullException(nameof(uiProvider));
    }

    /// <summary>
    /// Resolves a semantic element description to an ActionTarget.
    /// </summary>
    public async Task<ActionTarget> ResolveElementAsync(string description, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be null or empty.", nameof(description));

        // In simulation mode, generate deterministic mock coordinates and selectors
        if (_uiProvider.IsSimulationMode)
        {
            int hash = Math.Abs(description.GetHashCode());
            int x = (hash % 800) + 100;
            int y = (hash % 600) + 100;
            return new ActionTarget
            {
                Selector = $"[data-semantic='{description.ToLowerInvariant().Replace(" ", "-")}']",
                Text = description,
                X = x,
                Y = y
            };
        }

        // If using Windows UI Automation, delegate search to the provider
        if (_uiProvider is WindowsUiAutomationProvider winProvider)
        {
            return await winProvider.ResolveSemanticElementAsync(description, ct);
        }

        // Standard fallback target
        return new ActionTarget
        {
            Selector = $"[data-semantic='{description}']",
            Text = description
        };
    }
}
