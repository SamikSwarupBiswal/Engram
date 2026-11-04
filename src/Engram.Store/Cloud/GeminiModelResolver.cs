using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Engram.Store.Cloud;

/// <summary>
/// Resolves a usable Gemini text model at runtime. Explicit configuration always wins;
/// otherwise the Gemini models API is queried so Engram does not depend on a model name
/// compiled into the application.
/// </summary>
public sealed class GeminiModelResolver
{
    private const string ModelsEndpoint = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000";
    private readonly HttpClient _http;

    public GeminiModelResolver(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<string> ResolveAsync(string apiKey, string? configuredModel = null,
        CancellationToken cancellationToken = default)
    {
        var candidates = await ResolveCandidatesAsync(apiKey, configuredModel, cancellationToken);
        return candidates[0];
    }

    public async Task<IReadOnlyList<string>> ResolveCandidatesAsync(string apiKey, string? configuredModel = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(configuredModel))
            return new[] { NormalizeName(configuredModel) };
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("A Gemini API key or an explicitly configured model is required.");

        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gemini model discovery failed with HTTP {(int)response.StatusCode}.");

        var payload = await response.Content.ReadFromJsonAsync<ModelsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Gemini model discovery returned an empty response.");
        var selected = payload.Models
            .Where(IsPlanningModel)
            .OrderByDescending(Score)
            .ThenBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .Select(model => NormalizeName(model.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return selected.Length == 0
            ? throw new InvalidOperationException("Gemini did not advertise a Flash text model with generateContent support.")
            : selected;
    }

    private static bool IsPlanningModel(GeminiModel model)
    {
        var name = NormalizeName(model.Name);
        return model.SupportedGenerationMethods.Any(method =>
                   method.Equals("generateContent", StringComparison.OrdinalIgnoreCase)) &&
               name.Contains("flash", StringComparison.OrdinalIgnoreCase) &&
               !Regex.IsMatch(name, "(?:live|tts|image|embedding|vision)", RegexOptions.IgnoreCase);
    }

    private static long Score(GeminiModel model)
    {
        var name = NormalizeName(model.Name);
        var lifecycle = Regex.IsMatch(name, "(?:preview|experimental|exp)", RegexOptions.IgnoreCase) ? 0L : 1_000_000_000L;
        var fullFlash = name.Contains("flash-lite", StringComparison.OrdinalIgnoreCase) ? 0L : 100_000_000L;
        var version = Regex.Match(name, @"gemini-(?<major>\d+)(?:\.(?<minor>\d+))?");
        var major = version.Success ? long.Parse(version.Groups["major"].Value) : 0;
        var minor = version.Groups["minor"].Success ? long.Parse(version.Groups["minor"].Value) : 0;
        return lifecycle + fullFlash + major * 1_000_000L + minor * 10_000L + Math.Min(model.OutputTokenLimit, 9_999);
    }

    private static string NormalizeName(string value) =>
        value.Trim().StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? value.Trim()[7..] : value.Trim();

    private sealed class ModelsResponse
    {
        [JsonPropertyName("models")]
        public List<GeminiModel> Models { get; init; } = new();
    }

    private sealed class GeminiModel
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("supportedGenerationMethods")]
        public List<string> SupportedGenerationMethods { get; init; } = new();

        [JsonPropertyName("outputTokenLimit")]
        public int OutputTokenLimit { get; init; }
    }
}
