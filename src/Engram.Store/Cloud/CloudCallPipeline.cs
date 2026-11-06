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
    private readonly IReadOnlyList<ICloudModelProvider> _availableProviders;

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
        _availableProviders = (providers ?? throw new ArgumentNullException(nameof(providers))).ToArray();
        foreach (var provider in _availableProviders)
        {
            var target = provider.ProviderName switch
            {
                "gemini-flash" => ComputeTarget.GeminiFlash,
                "claude-sonnet" => ComputeTarget.ClaudeSonnet,
                "mock" => ComputeTarget.GeminiFlash, // mock routes to GeminiFlash target
                _ => ComputeTarget.GeminiFlash
            };
            _providers[target] = provider;
            if (provider.ProviderName is not ("gemini-flash" or "claude-sonnet" or "mock"))
                _providers[ComputeTarget.ClaudeSonnet] = provider;
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

        // Resolve fallbacks before disclosing retrieved output to its original provider.
        var provider = ResolveProvider(routing.Target);
        if (provider == null)
        {
            AuditBlockedCall(callRequest, routing, $"No available cloud provider can satisfy {routing.Target}.");
            return PipelineResult.Blocked($"No available cloud provider can satisfy {routing.Target}.");
        }
        var hasScopedMemory = callRequest.Messages.Any(message => message.MemoryProvider != null);
        var disclosureIdentity = provider.DisclosureIdentity;
        if (hasScopedMemory)
        {
            var messages = callRequest.Messages.Where(message => message.MemoryProvider == null ||
                message.MemoryProvider != "local" && !string.IsNullOrWhiteSpace(disclosureIdentity) &&
                string.Equals(message.MemoryProvider, disclosureIdentity, StringComparison.Ordinal)).ToArray();
            // Rebuild Payload too: it may contain redundant copies of excluded messages.
            callRequest = new CloudCallRequest
            {
                Reason = callRequest.Reason, Complexity = callRequest.Complexity,
                Payload = string.Join("\n", messages.Select(message => $"{message.Role}: {message.Content}")),
                Messages = messages, PrivacyClass = callRequest.PrivacyClass,
                MaxTokens = callRequest.MaxTokens, Metadata = callRequest.Metadata
            };
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

        // Resolve the actual provider before budgeting. A configured fallback (for example,
        // Gemini handling a request initially classified as high complexity) must not be
        // priced as a provider that is not installed.
        // Step 5: Budget check
        var estimatedCost = EstimateCost(provider, filterResult.FilteredPayload, callRequest.MaxTokens);
        var budgetCheck = _budgetManager.CheckBudget(estimatedCost);
        if (!budgetCheck.IsAllowed)
        {
            AuditBlockedCall(callRequest, routing, budgetCheck.DenyReason!);
            return PipelineResult.Blocked(budgetCheck.DenyReason!);
        }

        // Step 6: Check cache
        var cacheKey = ComputeCacheKey(callRequest, provider);
        if (!hasScopedMemory && _cache.TryGet(cacheKey, out var cachedEntry) && cachedEntry is not null)
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
                DisclosureIdentity = disclosureIdentity,
                RoutingReason = routing.Reason,
                FilterReason = filterResult.Reason
            };
        }

        // Step 7: Execute the selected provider.
        _rateLimiter.RecordCall();

        var cloudRequest = new CloudModelRequest
        {
            Reason = callRequest.Reason,
            Complexity = callRequest.Complexity,
            Payload = filterResult.FilteredPayload,
            Messages = FilterMessages(callRequest.Messages, callRequest.PrivacyClass),
            MaxTokens = callRequest.MaxTokens,
            OriginalPrivacyClass = callRequest.PrivacyClass,
            ContainsScopedMemory = hasScopedMemory,
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
            LogAuditEntry(callRequest, routing, provider, filterResult, 0, 0, 0, false, ex.Message,
                suppressPayload: hasScopedMemory);
            return PipelineResult.Failed(hasScopedMemory ? "The cloud call with task context failed. Try again." : $"Cloud call failed: {ex.Message}");
        }

        // Step 8: Log to audit
        LogAuditEntry(callRequest, routing, provider, filterResult,
            response.InputTokens, response.OutputTokens, response.CostEstimate,
            response.Success, response.ErrorMessage, response.Model, suppressPayload: hasScopedMemory);

        if (!response.Success)
        {
            return PipelineResult.Failed(hasScopedMemory ? "The cloud call with task context failed. Try again." : response.ErrorMessage ?? "Cloud call failed.");
        }

        // Cache successful response (if not private)
        if (!hasScopedMemory && callRequest.PrivacyClass != PrivacyClass.Private &&
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
            DisclosureIdentity = disclosureIdentity,
            RoutingReason = routing.Reason,
            FilterReason = filterResult.Reason
        };
    }

    private ICloudModelProvider? ResolveProvider(ComputeTarget target)
    {
        if (_providers.TryGetValue(target, out var exact) && exact.IsAvailable) return exact;
        return _availableProviders.FirstOrDefault(candidate => candidate.IsAvailable);
    }

    private IReadOnlyList<CloudModelMessage> FilterMessages(IReadOnlyList<CloudModelMessage> messages, PrivacyClass privacyClass)
    {
        if (messages.Count == 0) return Array.Empty<CloudModelMessage>();
        var filtered = new List<CloudModelMessage>(messages.Count);
        foreach (var message in messages)
        {
            var result = _filter.FilterText(message.Content, privacyClass);
            if (!result.IsAllowed) return Array.Empty<CloudModelMessage>();
            filtered.Add(new CloudModelMessage(message.Role, result.FilteredPayload));
        }
        return filtered;
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
        string? errorMessage,
        string? responseModel = null,
        bool suppressPayload = false)
    {
        _auditLog.Log(new CloudAuditEntry
        {
            Reason = request.Reason,
            Provider = provider.ProviderName,
            Model = string.IsNullOrWhiteSpace(responseModel) ? provider.ModelName : responseModel,
            PayloadSummary = suppressPayload ? "[Provider-scoped task memory excluded from audit payload]" : filterResult.FilteredPayload.Length > 200
                ? filterResult.FilteredPayload[..200] + "..."
                : filterResult.FilteredPayload,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsd = cost,
            Success = success,
            ErrorMessage = suppressPayload && errorMessage != null ? "Provider-scoped call failed; details omitted." : errorMessage,
            TaskComplexity = request.Complexity.ToString(),
            ComputeTarget = routing.Target.ToString()
        });
    }

    private static decimal EstimateCost(ICloudModelProvider provider, string payload, int maximumOutputTokens)
    {
        var inputTokens = Math.Max(1, payload.Length / 4);
        var outputTokens = Math.Max(1, maximumOutputTokens);
        return provider.ProviderName.ToLowerInvariant() switch
        {
            "gemini-flash" => (inputTokens * 0.000000075m) + (outputTokens * 0.0000003m),
            "claude-sonnet" => (inputTokens * 0.000003m) + (outputTokens * 0.000015m),
            _ => (inputTokens * 0.000001m) + (outputTokens * 0.000002m)
        };
    }

    private static string ComputeCacheKey(CloudCallRequest request, ICloudModelProvider provider)
    {
        // Include provider/model and generation settings so a truncated response from an old
        // token budget cannot poison a later planner request.
        var settings = string.Join(";", request.Metadata.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}"));
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{provider.ProviderName}|{provider.DisclosureIdentity}|{provider.ModelName}|{request.MaxTokens}|{settings}|{request.Payload}"));
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

    /// <summary>Optional role-preserving messages. The pipeline sanitizes each message before dispatch.</summary>
    public IReadOnlyList<CloudModelMessage> Messages { get; init; } = Array.Empty<CloudModelMessage>();

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
    public string? DisclosureIdentity { get; init; }
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
