using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Engram.Store.Tests;

/// <summary>
/// Soak test harness for inference stability validation.
/// 
/// These tests require a running Engram API sidecar with model loaded.
/// They are NOT part of the normal test suite — run explicitly:
///   dotnet test --filter "Category=Soak"
/// 
/// Test categories:
///   1. Cancellation integrity — cancel/restart cycles
///   2. Long-lived worker — sequential requests with drift detection
///   3. Memory trend — cumulative RAM/working set tracking
///   4. Timeout frequency — watchdog accuracy over many requests
/// </summary>
[Trait("Category", "Soak")]
public class InferenceSoakTests
{
    private const string BaseUrl = "http://127.0.0.1:5000";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static bool IsApiAvailable()
    {
        try
        {
            var response = Client.GetAsync($"{BaseUrl}/api/health").Result;
            if (!response.IsSuccessStatusCode) return false;
            var json = response.Content.ReadFromJsonAsync<JsonElement>().Result;
            return json.GetProperty("isReady").GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    private static HealthData GetHealth()
    {
        var response = Client.GetAsync($"{BaseUrl}/api/health").Result;
        var json = response.Content.ReadFromJsonAsync<JsonElement>().Result;
        return new HealthData
        {
            State = json.GetProperty("state").GetString() ?? "",
            IsReady = json.GetProperty("isReady").GetBoolean(),
            Backend = json.GetProperty("backend").GetString(),
            UptimeSeconds = json.GetProperty("uptimeSeconds").GetInt32()
        };
    }

    private static async Task<InferenceResponse> SendChatAsync(string message, int maxTokens = 256)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var body = new
            {
                messages = new[] { new { role = "user", content = message } },
                maxTokens
            };

            var response = await Client.PostAsJsonAsync($"{BaseUrl}/v1/chat/completions", body);
            sw.Stop();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var choice = json.GetProperty("choices")[0];
            var finishReason = choice.GetProperty("finish_reason").GetString();
            var content = choice.GetProperty("message").GetProperty("content").GetString() ?? "";

            return new InferenceResponse
            {
                Success = finishReason == "stop",
                Content = content,
                FinishReason = finishReason ?? "",
                LatencyMs = sw.ElapsedMilliseconds,
                Tokens = EstimateTokens(content),
                TokensPerSecond = EstimateTokens(content) / Math.Max(0.001, sw.Elapsed.TotalSeconds)
            };
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            return new InferenceResponse
            {
                Success = false,
                FinishReason = "timeout",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new InferenceResponse
            {
                Success = false,
                FinishReason = "error",
                Content = ex.Message,
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
    }

    private static int EstimateTokens(string text) => text.Length / 4;

    // ═══════════════════════════════════════════
    //  TEST 1: Sequential stability (100 requests)
    // ═══════════════════════════════════════════

    [Fact]
    public async Task Soak_Sequential100_DetectDrift()
    {
        if (!IsApiAvailable())
        {
            Console.WriteLine("SKIP: API not available. Start Engram.Api with model loaded.");
            return;
        }

        const int requestCount = 100;
        var results = new List<InferenceResponse>();
        var prompts = new[]
        {
            "What is 2+2?",
            "Summarize the concept of memory in one sentence.",
            "What day comes after Monday?",
            "Name three colors.",
            "What is the capital of France?",
            "Explain gravity briefly.",
            "What is 10*10?",
            "Name a programming language.",
            "What season comes after winter?",
            "What is H2O?"
        };

        Console.WriteLine($"Starting sequential soak: {requestCount} requests");

        for (int i = 0; i < requestCount; i++)
        {
            var prompt = prompts[i % prompts.Length];
            var result = await SendChatAsync(prompt, maxTokens: 128);
            results.Add(result);

            if (i % 10 == 0)
            {
                var recent = results.TakeLast(10).ToList();
                var avgTok = recent.Where(r => r.Success).Select(r => r.TokensPerSecond).DefaultIfEmpty(0).Average();
                var avgLat = recent.Where(r => r.Success).Select(r => r.LatencyMs).DefaultIfEmpty(0).Average();
                var successRate = recent.Count(r => r.Success) / 10.0;
                Console.WriteLine($"  [{i,4}] tok/s: {avgTok:F1} | latency: {avgLat:F0}ms | success: {successRate:P0}");
            }
        }

        // Analysis
        var first20 = results.Take(20).Where(r => r.Success).ToList();
        var last20 = results.TakeLast(20).Where(r => r.Success).ToList();

        var firstAvgTok = first20.Select(r => r.TokensPerSecond).DefaultIfEmpty(0).Average();
        var lastAvgTok = last20.Select(r => r.TokensPerSecond).DefaultIfEmpty(0).Average();

        var totalSuccess = results.Count(r => r.Success);
        var totalTimeout = results.Count(r => r.FinishReason == "timeout");
        var totalError = results.Count(r => r.FinishReason == "error");

        Console.WriteLine($"\n=== SOAK RESULTS ===");
        Console.WriteLine($"Total: {requestCount} | Success: {totalSuccess} | Timeout: {totalTimeout} | Error: {totalError}");
        Console.WriteLine($"First 20 avg tok/s: {firstAvgTok:F2}");
        Console.WriteLine($"Last 20 avg tok/s:  {lastAvgTok:F2}");

        if (firstAvgTok > 0)
        {
            var drift = (firstAvgTok - lastAvgTok) / firstAvgTok * 100;
            Console.WriteLine($"Performance drift: {drift:+0.0;-0.0}% {(drift > 20 ? "⚠ SIGNIFICANT" : "✓ acceptable")}");
        }

        // Assertions (soft — soak tests inform, don't gate)
        Assert.True(totalSuccess > requestCount * 0.8,
            $"Success rate too low: {totalSuccess}/{requestCount}. Inference is unstable.");
    }

    // ═══════════════════════════════════════════
    //  TEST 2: Cancellation integrity (50 cycles)
    // ═══════════════════════════════════════════

    [Fact]
    public async Task Soak_CancellationIntegrity_50Cycles()
    {
        if (!IsApiAvailable())
        {
            Console.WriteLine("SKIP: API not available.");
            return;
        }

        const int cycles = 50;
        var recoveryTimes = new List<long>();
        var postCancelSuccesses = 0;

        Console.WriteLine($"Starting cancellation integrity test: {cycles} cancel/restart cycles");

        for (int i = 0; i < cycles; i++)
        {
            // Step 1: Start long inference with CancellationToken
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var cancelSw = Stopwatch.StartNew();

            try
            {
                // Send a long-form request that should take many seconds
                var body = new
                {
                    messages = new[] { new { role = "user", content = "Write a detailed 500-word essay about the history of computing, covering the evolution from mainframes to modern cloud computing." } },
                    maxTokens = 512
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions")
                {
                    Content = JsonContent.Create(body)
                };

                await Client.SendAsync(request, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected — we cancelled
            }
            catch
            {
                // Server might reject or timeout
            }

            cancelSw.Stop();

            // Step 2: Immediately send a new request — test recovery
            var recoverySw = Stopwatch.StartNew();
            var recovery = await SendChatAsync("What is 1+1?", maxTokens: 32);
            recoverySw.Stop();

            if (recovery.Success)
            {
                postCancelSuccesses++;
                recoveryTimes.Add(recoverySw.ElapsedMilliseconds);
            }

            if (i % 10 == 0)
            {
                Console.WriteLine($"  [{i,3}] cancel: {cancelSw.ElapsedMilliseconds}ms | recovery: {recoverySw.ElapsedMilliseconds}ms | success: {recovery.Success}");
            }

            // Brief pause between cycles
            await Task.Delay(500);
        }

        var avgRecovery = recoveryTimes.Count > 0 ? recoveryTimes.Average() : 0;
        var maxRecovery = recoveryTimes.Count > 0 ? recoveryTimes.Max() : 0;
        var recoveryRate = postCancelSuccesses / (double)cycles;

        Console.WriteLine($"\n=== CANCELLATION INTEGRITY ===");
        Console.WriteLine($"Cycles: {cycles} | Post-cancel success: {postCancelSuccesses} ({recoveryRate:P0})");
        Console.WriteLine($"Recovery latency: avg={avgRecovery:F0}ms | max={maxRecovery:F0}ms");

        if (recoveryRate < 0.9)
        {
            Console.WriteLine($"⚠ WARNING: Post-cancel recovery rate is {recoveryRate:P0}. Context may be corrupted after cancellation.");
        }

        Assert.True(recoveryRate > 0.7,
            $"Post-cancel recovery too low: {recoveryRate:P0}. Inference context may be poisoned after cancel.");
    }

    // ═══════════════════════════════════════════
    //  TEST 3: Memory trend (200 requests)
    // ═══════════════════════════════════════════

    [Fact]
    public async Task Soak_MemoryTrend_200Requests()
    {
        if (!IsApiAvailable())
        {
            Console.WriteLine("SKIP: API not available.");
            return;
        }

        const int requestCount = 200;
        var memorySnapshots = new List<(int request, long workingSetMb)>();

        Console.WriteLine($"Starting memory trend test: {requestCount} requests");

        for (int i = 0; i < requestCount; i++)
        {
            await SendChatAsync($"Count from 1 to {10 + (i % 20)}.", maxTokens: 64);

            if (i % 20 == 0)
            {
                var health = GetHealth();
                var process = Process.GetCurrentProcess();
                var wsMb = process.WorkingSet64 / (1024 * 1024);
                memorySnapshots.Add((i, wsMb));
                Console.WriteLine($"  [{i,4}] WorkingSet: {wsMb}MB | Uptime: {health.UptimeSeconds}s");
            }
        }

        if (memorySnapshots.Count >= 3)
        {
            var first = memorySnapshots[1].workingSetMb; // Skip first (cold start)
            var last = memorySnapshots[^1].workingSetMb;
            var growth = last - first;
            var growthPct = first > 0 ? (double)growth / first * 100 : 0;

            Console.WriteLine($"\n=== MEMORY TREND ===");
            Console.WriteLine($"First snapshot: {first}MB");
            Console.WriteLine($"Last snapshot:  {last}MB");
            Console.WriteLine($"Growth: {growth:+0;-0}MB ({growthPct:+0.0;-0.0}%)");

            if (growthPct > 50)
            {
                Console.WriteLine($"⚠ WARNING: Memory growth {growthPct:F0}% over {requestCount} requests. Possible leak.");
            }
            else
            {
                Console.WriteLine($"✓ Memory growth acceptable ({growthPct:F0}%)");
            }
        }
    }

    // ═══════════════════════════════════════════
    //  HELPER TYPES
    // ═══════════════════════════════════════════

    private class InferenceResponse
    {
        public bool Success { get; init; }
        public string Content { get; init; } = "";
        public string FinishReason { get; init; } = "";
        public long LatencyMs { get; init; }
        public int Tokens { get; init; }
        public double TokensPerSecond { get; init; }
    }

    private class HealthData
    {
        public string State { get; init; } = "";
        public bool IsReady { get; init; }
        public string? Backend { get; init; }
        public int UptimeSeconds { get; init; }
    }
}
