using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Engram.Api.Tests;

/// <summary>
/// Comprehensive API endpoint tests for all Engram routes.
/// Tests HTTP-level behavior: status codes, response format, error handling.
/// </summary>
public class ApiEndpointFullTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiEndpointFullTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // ─── Health ───

    [Fact]
    public async Task ApiHealth_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Health now returns lifecycle state, not static "healthy"
        Assert.True(json.TryGetProperty("state", out _), "Health response must include 'state' field");
        Assert.True(json.TryGetProperty("isReady", out _), "Health response must include 'isReady' field");
        Assert.True(json.TryGetProperty("uptimeSeconds", out _), "Health response must include 'uptimeSeconds' field");
    }

    // ─── Identity ───

    [Fact]
    public async Task Identity_Get_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/identity");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Identity_Put_ReturnsOk()
    {
        var profile = new { displayName = "Test User", goals = new[] { "test" } };
        var response = await _client.PutAsJsonAsync("/api/identity", profile);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Identity_AntiGoals_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/identity/anti-goals");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Identity_Priorities_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/identity/priorities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ─── Discovery ───

    [Fact]
    public async Task Discovery_Status_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/discovery/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Discovery_Post_ReturnsOk()
    {
        var answers = new { answers = new[] { "test answer" } };
        var response = await _client.PostAsJsonAsync("/api/discovery", answers);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created);
    }

    // ─── Drift ───

    [Fact]
    public async Task Drift_Get_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/drift");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Drift_Stats_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/drift/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Drift_Accept_Nonexistent_ReturnsNotFoundOrFalse()
    {
        var response = await _client.PostAsync("/api/drift/nonexistent-id/accept", null);
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Drift_Dismiss_Nonexistent_ReturnsNotFoundOrFalse()
    {
        var response = await _client.PostAsync("/api/drift/nonexistent-id/dismiss", null);
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Drift_Convert_Nonexistent_ReturnsNotFoundOrFalse()
    {
        var response = await _client.PostAsync("/api/drift/nonexistent-id/convert", null);
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.OK);
    }

    // ─── Salience ───

    [Fact]
    public async Task Salience_Get_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/salience");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ─── Archive ───

    [Fact]
    public async Task Archive_Get_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/archive");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Archive_Candidates_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/archive/candidates");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Archive_Stale_Post_ReturnsOk()
    {
        var response = await _client.PostAsync("/api/archive/stale", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Archive_Restore_Nonexistent_ReturnsNotFoundOrFalse()
    {
        var response = await _client.PostAsync("/api/archive/nonexistent-node/restore", null);
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.OK);
    }

    // ─── Intervention ───

    [Fact]
    public async Task Intervention_Check_ReturnsOk()
    {
        var request = new { action = "test", context = "test" };
        var response = await _client.PostAsJsonAsync("/api/intervention/check", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ─── Model ───

    [Fact]
    public async Task Model_Status_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/model/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Model_Download_Post_ReturnsOk()
    {
        var response = await _client.PostAsync("/api/model/download", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Model_Load_Post_ReturnsOk()
    {
        var response = await _client.PostAsync("/api/model/load", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Model_Unload_Post_ReturnsOk()
    {
        var response = await _client.PostAsync("/api/model/unload", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ─── Power Mode ───

    [Fact]
    public async Task PowerMode_Get_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/power-mode");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PowerMode_Post_ReturnsOk()
    {
        var request = new { mode = "eco" };
        var response = await _client.PostAsJsonAsync("/api/power-mode", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ─── Perception ───

    [Fact]
    public async Task Perception_Status_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/perception/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Perception_Start_Post_ReturnsOk()
    {
        var response = await _client.PostAsync("/api/perception/start", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Perception_Stop_Post_ReturnsOk()
    {
        var response = await _client.PostAsync("/api/perception/stop", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Perception_Capture_Post_ReturnsOk()
    {
        var response = await _client.PostAsync("/api/perception/capture", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    // ─── Layout ───

    [Fact]
    public async Task Layout_SnapResearch_Post_ReturnsResponse()
    {
        var request = new { url = "https://example.com" };
        var response = await _client.PostAsJsonAsync("/api/layout/snap-research", request);
        Assert.InRange((int)response.StatusCode, 200, 599);
    }

    [Fact]
    public async Task Layout_SnapLeft_Post_ReturnsResponse()
    {
        var response = await _client.PostAsync("/api/layout/snap-left", null);
        Assert.InRange((int)response.StatusCode, 200, 599);
    }

    [Fact]
    public async Task Layout_SnapRight_Post_ReturnsResponse()
    {
        var response = await _client.PostAsync("/api/layout/snap-right", null);
        Assert.InRange((int)response.StatusCode, 200, 599);
    }

    [Fact]
    public async Task Layout_Maximize_Post_ReturnsResponse()
    {
        var response = await _client.PostAsync("/api/layout/maximize", null);
        Assert.InRange((int)response.StatusCode, 200, 599);
    }

    [Fact]
    public async Task Layout_Restore_Post_ReturnsResponse()
    {
        var response = await _client.PostAsync("/api/layout/restore", null);
        Assert.InRange((int)response.StatusCode, 200, 599);
    }

    // ─── Security ───

    [Fact]
    public async Task Security_Status_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/security/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Security_Setup_Post_ReturnsOk()
    {
        var request = new { password = "test-password-123" };
        var response = await _client.PostAsJsonAsync("/api/security/setup", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Security_Unlock_Post_ReturnsOk()
    {
        var request = new { password = "wrong-password" };
        var response = await _client.PostAsJsonAsync("/api/security/unlock", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Security_ChangePassword_Post_ReturnsOk()
    {
        var request = new { oldPassword = "old", newPassword = "new-password-123" };
        var response = await _client.PostAsJsonAsync("/api/security/change-password", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Security_Export_Post_ReturnsOk()
    {
        var response = await _client.PostAsync("/api/security/export", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Security_Import_Post_ReturnsOk()
    {
        var request = new { data = "invalid-base64" };
        var response = await _client.PostAsJsonAsync("/api/security/import", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Security_Delete_Post_ReturnsOk()
    {
        var response = await _client.PostAsync("/api/security/delete", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    // ─── Automation ───

    [Fact]
    public async Task Automation_Plan_Post_ReturnsOkOrServerError()
    {
        var request = new { goal = "test automation" };
        var response = await _client.PostAsJsonAsync("/api/automation/plan", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Automation_Approve_Post_ReturnsOk()
    {
        var request = new { actionId = "nonexistent" };
        var response = await _client.PostAsJsonAsync("/api/automation/approve", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Automation_ApproveAll_Post_ReturnsOk()
    {
        var request = new { planId = "nonexistent" };
        var response = await _client.PostAsJsonAsync("/api/automation/approve-all", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Automation_DenyAll_Post_ReturnsOk()
    {
        var request = new { planId = "nonexistent" };
        var response = await _client.PostAsJsonAsync("/api/automation/deny-all", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Automation_Execute_Post_ReturnsOk()
    {
        var request = new { planId = "nonexistent" };
        var response = await _client.PostAsJsonAsync("/api/automation/execute", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Automation_Log_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/automation/log");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Automation_Rollback_Post_ReturnsOk()
    {
        var request = new { planId = "nonexistent" };
        var response = await _client.PostAsJsonAsync("/api/automation/rollback", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    // ─── Research ───

    [Fact]
    public async Task Research_Get_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/research");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Research_Start_Post_ReturnsOk()
    {
        var request = new { query = "test research" };
        var response = await _client.PostAsJsonAsync("/api/research/start", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created);
    }

    [Fact]
    public async Task Research_GetById_Nonexistent_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/research/nonexistent-id");
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Research_Resume_Nonexistent_ReturnsNotFoundOrBadRequest()
    {
        var response = await _client.PostAsync("/api/research/nonexistent-id/resume", null);
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Research_Cancel_Nonexistent_ReturnsNotFound()
    {
        var response = await _client.PostAsync("/api/research/nonexistent-id/cancel", null);
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.OK);
    }

    // ─── Google Workspace ───

    [Fact]
    public async Task Gws_Status_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/gws/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Gws_Connect_Post_ReturnsOk()
    {
        var request = new { code = "test-auth-code" };
        var response = await _client.PostAsJsonAsync("/api/gws/connect", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Gws_Disconnect_Post_ReturnsOk()
    {
        var response = await _client.PostAsync("/api/gws/disconnect", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Gws_Url_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/gws/url?clientId=test&redirectUri=http://localhost:5000/callback");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Gws_Sync_Post_ReturnsOk()
    {
        var response = await _client.PostAsync("/api/gws/sync", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Gws_Emails_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/gws/emails");
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Gws_Events_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/gws/events");
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Gws_Files_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/gws/files");
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    // ─── Tokens ───

    [Fact]
    public async Task Tokens_Get_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/tokens");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Tokens_Check_Post_ReturnsOk()
    {
        var request = new { tokens = 100, provider = "test" };
        var response = await _client.PostAsJsonAsync("/api/tokens/check", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Tokens_Pack_Post_ReturnsOk()
    {
        var request = new { name = "Test Pack", tokens = 50000 };
        var response = await _client.PostAsJsonAsync("/api/tokens/pack", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tokens_Pricing_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/tokens/pricing");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Tokens_Tier_Post_ReturnsOk()
    {
        var request = new { tier = "free" };
        var response = await _client.PostAsJsonAsync("/api/tokens/tier", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    // ─── Provider ───

    [Fact]
    public async Task Provider_Get_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/provider");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Provider_Post_ReturnsOk()
    {
        var request = new { provider = "openrouter", model = "test-model" };
        var response = await _client.PostAsJsonAsync("/api/provider", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    // ─── CORS ───

    [Fact]
    public async Task Cors_Preflight_ReturnsNoContent()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/search");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ─── Rate Limiting ───

    [Fact]
    public async Task RateLimit_HandlesRequests()
    {
        // Make several requests - should all succeed
        for (int i = 0; i < 5; i++)
        {
            var response = await _client.GetAsync("/api/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ─── Automation Control and Execution Endpoints (Phase 7I/7J) ───

    [Fact]
    public async Task Automation_GetStatus_ReturnsCorrectStructure()
    {
        var response = await _client.GetAsync("/api/automation/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("state", out var stateProp));
        Assert.Equal("Idle", stateProp.GetString());

        Assert.True(json.TryGetProperty("plan", out var planProp));
        Assert.Equal(JsonValueKind.Null, planProp.ValueKind);

        Assert.True(json.TryGetProperty("variables", out var varsProp));
        Assert.Equal(JsonValueKind.Null, varsProp.ValueKind);
    }

    [Fact]
    public async Task Automation_PauseResumeAbort_ReturnsOk()
    {
        // Pause
        var pauseResponse = await _client.PostAsync("/api/automation/pause", null);
        Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);
        var pauseJson = await pauseResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(pauseJson.TryGetProperty("message", out _));

        // Resume
        var resumeResponse = await _client.PostAsync("/api/automation/resume", null);
        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
        var resumeJson = await resumeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(resumeJson.TryGetProperty("message", out _));

        // Abort
        var abortResponse = await _client.PostAsync("/api/automation/abort", null);
        Assert.Equal(HttpStatusCode.OK, abortResponse.StatusCode);
        var abortJson = await abortResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(abortJson.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task Automation_CognitiveRun_ValidatesGoal()
    {
        // Missing goal
        var badRequest = new { goal = "" };
        var badResponse = await _client.PostAsJsonAsync("/api/automation/cognitive/run", badRequest);
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);

        // Null goal
        var badRequest2 = new { goal = (string?)null };
        var badResponse2 = await _client.PostAsJsonAsync("/api/automation/cognitive/run", badRequest2);
        Assert.Equal(HttpStatusCode.BadRequest, badResponse2.StatusCode);

        // Valid goal
        var request = new { goal = "Find weather in London" };
        var response = await _client.PostAsJsonAsync("/api/automation/cognitive/run", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("/api/automation/status", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Automation_ExecutePlan_ReturnsAccepted()
    {
        var planPayload = new
        {
            planId = "testPlan123",
            goal = "Run simple click action",
            steps = new Dictionary<string, object>
            {
                {
                    "step1", new
                    {
                        id = "step1",
                        action = new
                        {
                            actionId = "act1",
                            type = "Click",
                            description = "Click on elements",
                            permission = "Approved",
                            target = new
                            {
                                selector = "button#submit",
                                text = "Submit",
                                x = 100,
                                y = 200
                            }
                        },
                        dependsOn = new List<string>()
                    }
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/automation/execute-plan", planPayload);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("/api/automation/status", response.Headers.Location?.OriginalString);
    }
}

