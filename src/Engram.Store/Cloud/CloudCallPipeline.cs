using Engram.Store;

namespace Engram.Store.Cloud;

/// <summary>
/// Orchestrates the mandatory cloud call pipeline.
/// Every cloud call MUST go through this pipeline — no direct provider calls allowed.
///
/// Pipeline flow:
///   1. ModelRouter selects compute target (local vs cloud)
///   2. LocalFilter sanitizes payload (strips private/PII data)
///   3. TierGuard verifies Pro tier access
///   4. CloudRateLimiter enforces request rate limits
///   5. BudgetManager enforces spending limits
///   6. CleanCache check for cached responses
///   7. ICloudModelProvider.SendAsync (actual cloud call)
///   8. CloudAuditLog records every call with reason + cost
///
/// Derived from PRD:
///   - "Routine ingestion remains local by default"
///   - "Every cloud call has a reason, payload summary, provider, cost estimate, and result"
///   - "Private raw screen/clipboard/email data is never sent without explicit policy approval"
/// </summary>
public class CloudCallPipeline
{
    private readonly ModelRouter _router;
    private readonly LocalFilter _filter;
    private readonly TierGuard _tierGuard;
    private readonly CloudRateLimiter _rateLimiter;
    private readonly BudgetManager _budgetManager;
    private readonly CloudAuditLog _auditLog;
    private readonly CleanCache _cache;
    private readonly Dictionary<ComputeTarget, ICloudModelProvider> _providers;

    public CloudCallPipeline(
        ModelRouter router,
        LocalFilter filter,
        TierGuard tierGuard,
        CloudRateLimiter rateLimiter,
        BudgetManager budgetManager,
        CloudAuditLog auditLog,
        CleanCache cache,
        IEnumerable<ICloudModelProvider> providers)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _tierGuard = tierGuard ?? throw new ArgumentNullException(nameof(tierGuard));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _budgetManager = budgetManager ?? throw new ArgumentNullException(nameof(budgetManager));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));

        // Map providers by name for routing
        _providers = new Dictionary<ComputeTarget, ICloudModelProvider>();
        foreach (var provider in providers ?? throw new ArgumentNullException(nameof(providers)))
        {
            var target = provider.ProviderName switch
            {
                "gemini-flash" => ComputeTarget.GeminiFlash,
                "claude-sonnet" => ComputeTarget.ClaudeSonnet,
                "mock" => ComputeTarget.GeminiFlash, // mock routes to GeminiFlash target
                _ => ComputeTarget.GeminiFlash
            };
            _providers[target] = provider;
        }
    }

    /// <summary>
    /// Execute the full cloud call pipeline.
    /// This is the ONLY way to make a cloud call — direct provider access is forbidden.
    /// </summary>
    public async Task<PipelineResult> ExecuteAsync(
        CloudCallRequest callRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callRequest);

        // Step 1: Route based on complexity
        var routing = _router.Route(callRequest.Complexity);
        if (!routing.IsCloud)
        {
            return PipelineResult.RoutedToLocal(routing.Reason);
        }

        // Step 2: Filter/sanitize the payload
        var filterResult = _filter.FilterText(callRequest.Payload, callRequest.PrivacyClass);
        if (!filterResult.IsAllowed)
        {
            // Audit the blocked attempt
            AuditBlockedCall(callRequest, routing, filterResult.Reason);
            return PipelineResult.Blocked(filterResult.Reason);
        }

        // Step 3: Verify tier access (defense-in-depth — router already checked)
        var tierGate = _tierGuard.CheckCloudAccess();
        if (!tierGate.IsAllowed)
        {
            AuditBlockedCall(callRequest, routing, tierGate.BlockReason!);
            return PipelineResult.Blocked(tierGate.BlockReason!);
        }

        // Step 4: Rate limit check
        var rateLimit = _rateLimiter.CheckRateLimit();
        if (!rateLimit.IsAllowed)
        {
            AuditBlockedCall(callRequest, routing, rateLimit.DenyReason!);
            return PipelineResult.Blocked(rateLimit.DenyReason!);
        }

        // Step 5: Budget check
        var estimatedCost = EstimateCost(routing.Target, filterResult.FilteredPayload);
        var budgetCheck = _budgetManager.CheckBudget(estimatedCost);
        if (!budgetCheck.IsAllowed)
        {
            AuditBlockedCall(callRequest, routing, budgetCheck.DenyReason!);
            return PipelineResult.Blocked(budgetCheck.DenyReason!);
        }

        // Step 6: Check cache
        var cacheKey = ComputeCacheKey(callRequest);
        if (_cache.TryGet(cacheKey, out var cachedEntry) && cachedEntry is not null)
        {
            // Record rate limiter hit for cache access too
            _rateLimiter.RecordCall();

            return new PipelineResult
            {
                Status = PipelineStatus.Completed,
                Content = cachedEntry.Response,
                Provider = cachedEntry.Provider,
                Model = cachedEntry.Model,
                CostEstimate = 0, // cached — no cost
                InputTokens = 0,
                OutputTokens = 0,
                FromCache = true,
                RoutingReason = routing.Reason,
                FilterReason = filterResult.Reason
            };
        }

        // Step 7: Find provider and execute cloud call
        if (!_providers.TryGetValue(routing.Target, out var provider))
        {
            AuditBlockedCall(callRequest, routing, $"No provider configured for {routing.Target}.");
            return PipelineResult.Blocked($"No provider configured for {routing.Target}.");
        }

        if (!provider.IsAvailable)
        {
            AuditBlockedCall(callRequest, routing, $"Provider {provider.ProviderName} is not available.");
            return PipelineResult.Blocked($"Provider {provider.ProviderName} is not available.");
        }

        // Record the call for rate limiting
        _rateLimiter.RecordCall();

        var cloudRequest = new CloudModelRequest
        {
            Reason = callRequest.Reason,
            Complexity = callRequest.Complexity,
            Payload = filterResult.FilteredPayload,
            MaxTokens = callRequest.MaxTokens,
            OriginalPrivacyClass = callRequest.PrivacyClass,
            Metadata = callRequest.Metadata
        };

        CloudModelResponse response;
        try
        {
            response = await provider.SendAsync(cloudRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log failed call
            LogAuditEntry(callRequest, routing, provider, filterResult, 0, 0, 0, false, ex.Message);
            return PipelineResult.Failed($"Cloud call failed: {ex.Message}");
        }

        // Step 8: Log to audit
        LogAuditEntry(callRequest, routing, provider, filterResult,
            response.InputTokens, response.OutputTokens, response.CostEstimate,
            response.Success, response.ErrorMessage);

        if (!response.Success)
        {
            return PipelineResult.Failed(response.ErrorMessage ?? "Cloud call failed.");
        }

        // Cache successful response (if not private)
        if (callRequest.PrivacyClass != PrivacyClass.Private &&
            callRequest.PrivacyClass != PrivacyClass.Sensitive)
        {
            _cache.Put(cacheKey, new CacheEntry
            {
                Key = cacheKey,
                Response = response.Content,
                Provider = response.Provider,
                Model = response.Model,
                CostUsd = response.CostEstimate
            }, callRequest.PrivacyClass);
        }

        return new PipelineResult
        {
            Status = PipelineStatus.Completed,
            Content = response.Content,
            Provider = response.Provider,
            Model = response.Model,
            CostEstimate = response.CostEstimate,
            InputTokens = response.InputTokens,
            OutputTokens = response.OutputTokens,
            FromCache = false,
            RoutingReason = routing.Reason,
            FilterReason = filterResult.Reason
        };
    }

    private void AuditBlockedCall(CloudCallRequest request, RoutingDecision routing, string reason)
    {
        _auditLog.Log(new CloudAuditEntry
        {
            Reason = request.Reason,
            Provider = "blocked",
            Model = "none",
            PayloadSummary = $"[BLOCKED] {reason}",
            CostUsd = 0,
            Success = false,
            ErrorMessage = reason,
            TaskComplexity = request.Complexity.ToString(),
            ComputeTarget = routing.Target.ToString()
        });
    }

    private void LogAuditEntry(
        CloudCallRequest request,
        RoutingDecision routing,
        ICloudModelProvider provider,
        FilterResult filterResult,
        int inputTokens,
        int outputTokens,
        decimal cost,
        bool success,
        string? errorMessage)
    {
        _auditLog.Log(new CloudAuditEntry
        {
            Reason = request.Reason,
            Provider = provider.ProviderName,
            Model = provider.ModelName,
            PayloadSummary = filterResult.FilteredPayload.Length > 200
                ? filterResult.FilteredPayload[..200] + "..."
                : filterResult.FilteredPayload,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsd = cost,
            Success = success,
            ErrorMessage = errorMessage,
            TaskComplexity = request.Complexity.ToString(),
            ComputeTarget = routing.Target.ToString()
        });
    }

    private static decimal EstimateCost(ComputeTarget target, string payload)
    {
        var inputTokens = payload.Length / 4; // rough estimate
        return target switch
        {
            ComputeTarget.GeminiFlash => (inputTokens * 0.000075m) + (200 * 0.0003m),
            ComputeTarget.ClaudeSonnet => (inputTokens * 0.003m) + (500 * 0.015m),
            _ => 0.001m
        };
    }

    private static string ComputeCacheKey(CloudCallRequest request)
    {
        // Simple cache key based on reason + payload hash
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(request.Payload));
        return $"{request.Complexity}:{Convert.ToHexString(hash)[..16]}";
    }
}

/// <summary>
/// Request to the cloud call pipeline.
/// Contains the raw data — the pipeline handles filtering, routing, and execution.
/// </summary>
public class CloudCallRequest
{
    /// <summary>Human-readable reason for this cloud call (audit trail).</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Task complexity for routing decisions.</summary>
    public TaskComplexity Complexity { get; init; } = TaskComplexity.Medium;

    /// <summary>Raw payload — pipeline will filter before sending to cloud.</summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>Privacy class of the payload data.</summary>
    public PrivacyClass PrivacyClass { get; init; } = PrivacyClass.Public;

    /// <summary>Maximum tokens to generate.</summary>
    public int MaxTokens { get; init; } = 1024;

    /// <summary>Metadata tags for routing and caching.</summary>
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
/// Result from the cloud call pipeline.
/// </summary>
public class PipelineResult
{
    public PipelineStatus Status { get; init; }
    public string Content { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public decimal CostEstimate { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public bool FromCache { get; init; }
    public string RoutingReason { get; init; } = string.Empty;
    public string FilterReason { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }

    public static PipelineResult RoutedToLocal(string reason) => new()
    {
        Status = PipelineStatus.RoutedToLocal,
        RoutingReason = reason
    };

    public static PipelineResult Blocked(string reason) => new()
    {
        Status = PipelineStatus.Blocked,
        ErrorMessage = reason
    };

    public static PipelineResult Failed(string error) => new()
    {
        Status = PipelineStatus.Failed,
        ErrorMessage = error
    };
}

public enum PipelineStatus
{
    /// <summary>Cloud call completed successfully.</summary>
    Completed,

    /// <summary>Task was routed to local compute (no cloud call needed).</summary>
    RoutedToLocal,

    /// <summary>Cloud call was blocked by filter, tier, budget, or rate limit.</summary>
    Blocked,

    /// <summary>Cloud call was attempted but failed (provider error, network, etc.).</summary>
    Failed
}
