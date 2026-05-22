using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Engram.Store.Events;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Automation;

public class EnvironmentalResilienceEngine
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<EnvironmentalResilienceEngine>? _logger;
    private readonly HttpClient _httpClient;
    private bool _isOffline;

    public bool IsOffline => _isOffline;

    public EnvironmentalResilienceEngine(IEventBus eventBus, ILogger<EnvironmentalResilienceEngine>? logger = null)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    }

    /// <summary>
    /// Check network connectivity.
    /// </summary>
    public async Task<bool> CheckNetworkConnectivityAsync(CancellationToken ct = default)
    {
        try
        {
            // Simple check by fetching a public endpoint (e.g., Google or Bing)
            var response = await _httpClient.GetAsync("https://www.google.com", ct);
            var wasOffline = _isOffline;
            _isOffline = !response.IsSuccessStatusCode;

            if (wasOffline && !_isOffline)
            {
                PublishNetworkStatus("Online");
            }
            else if (!wasOffline && _isOffline)
            {
                PublishNetworkStatus("Offline");
            }

            return !_isOffline;
        }
        catch
        {
            if (!_isOffline)
            {
                _isOffline = true;
                PublishNetworkStatus("Offline");
            }
            return false;
        }
    }

    /// <summary>
    /// Handles popup dismissal and network recovery.
    /// </summary>
    public async Task HandleDisturbancesAsync(
        BrowserAgentRuntime browser,
        IDesktopOperator desktop,
        CancellationToken ct = default)
    {
        // 1. Handle network drops
        while (ct.IsCancellationRequested == false && _isOffline)
        {
            _logger?.LogWarning("Resilience: Network is offline. Waiting 2 seconds before retrying...");
            await Task.Delay(2000, ct);
            await CheckNetworkConnectivityAsync(ct);
        }

        // 2. Handle unexpected popups/modals
        try
        {
            var (process, title) = await desktop.GetActiveWindowAsync(ct);
            if (!string.IsNullOrEmpty(title) && 
                (title.Contains("error", StringComparison.OrdinalIgnoreCase) || 
                 title.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                 title.Contains("alert", StringComparison.OrdinalIgnoreCase) ||
                 title.Contains("popup", StringComparison.OrdinalIgnoreCase) ||
                 title.Contains("modal", StringComparison.OrdinalIgnoreCase)))
            {
                _logger?.LogInformation("Resilience: Detected potential popup window '{Title}'. Attempting to dismiss via Escape key.", title);
                await desktop.KeyPressAsync("Escape", ct);

                _eventBus.Publish(new EventEnvelope
                {
                    EventType = "automation.resilience.popup_dismissed",
                    Source = "environmental_resilience_engine",
                    Payload = new { WindowTitle = title, Process = process }
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Resilience: Failed to check or dismiss OS-level popup.");
        }
    }

    public void TriggerSleepTransition()
    {
        _logger?.LogInformation("Resilience: System entering sleep mode.");
        _eventBus.Publish(new EventEnvelope
        {
            EventType = "system.sleep",
            Source = "environmental_resilience_engine"
        });
    }

    public void TriggerWakeTransition()
    {
        _logger?.LogInformation("Resilience: System woke up.");
        _eventBus.Publish(new EventEnvelope
        {
            EventType = "system.wake",
            Source = "environmental_resilience_engine"
        });
    }

    private void PublishNetworkStatus(string status)
    {
        _eventBus.Publish(new EventEnvelope
        {
            EventType = $"network.status.{status.ToLowerInvariant()}",
            Source = "environmental_resilience_engine",
            Payload = new { Status = status, Timestamp = DateTimeOffset.UtcNow }
        });
    }
}
