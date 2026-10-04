using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptEvidence;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// The provider-listed model limits recorded when a Claude attempt concluded, exposed only by the existing historical
/// attempt-evidence route, through the real authenticated MVC pipeline: an additive, nullable member that reads stored facts
/// alone, discloses nothing internal, and never reaches any other response. Background supervisors are removed by the
/// factory, so every attempt below is seeded before a request.
/// </summary>
public sealed class AgentModelContextLimitsEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string Source = "claude-cli-model-usage-v1";
    private static readonly string Fingerprint = new('a', 64);

    private static readonly HashSet<string> ModelFields = ["modelId", "contextWindowTokens", "maxOutputTokens"];

    private static AgentModelContextLimitsEvidence Limits(params AgentModelContextLimit[] models) =>
        AgentModelContextLimitsEvidence.Create(Source, models);

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    [Fact]
    public async Task The_evidence_route_exposes_every_listed_model_with_its_limits_in_ordinal_order_and_nothing_internal()
    {
        var runId = await SeedRunAsync();
        var attemptId = await SeedConcludedAsync(
            runId, 1, Limits(new AgentModelContextLimit("claude-b", 1000000, 64000), new AgentModelContextLimit("Claude-Z", 200000, 200000), new AgentModelContextLimit("claude-a", 200000, 32000)));

        var (status, body) = await GetEvidenceAsync(runId, attemptId);

        Assert.Equal(HttpStatusCode.OK, status);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var limits = root.GetProperty("modelContextLimits");
        Assert.Equal(["models"], limits.EnumerateObject().Select(property => property.Name));
        var models = limits.GetProperty("models").EnumerateArray().ToArray();
        Assert.Equal(["Claude-Z", "claude-a", "claude-b"], models.Select(model => model.GetProperty("modelId").GetString()));
        Assert.All(models, model => Assert.Equal(ModelFields, model.EnumerateObject().Select(property => property.Name).ToHashSet()));
        Assert.Equal([200000, 200000, 1000000], models.Select(model => model.GetProperty("contextWindowTokens").GetInt32()));
        Assert.Equal([200000, 32000, 64000], models.Select(model => model.GetProperty("maxOutputTokens").GetInt32()));

        // Nothing internal: no parsing-contract source, snapshot version, stored text, path, or session.
        Assert.DoesNotContain(Source, body, StringComparison.Ordinal);
        var limitsText = limits.GetRawText();
        Assert.DoesNotContain("source", limitsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("version", limitsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("snapshot", limitsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", body, StringComparison.Ordinal);
        Assert.Equal("ProviderInvocationFailed", root.GetProperty("outcome").GetString());
        Assert.True(root.GetProperty("identityValid").GetBoolean());
        Assert.Equal(1200, root.GetProperty("tokenUsage").GetProperty("inputTokens").GetInt32());
    }

    [Fact]
    public async Task An_attempt_without_recorded_limits_has_an_explicit_null_member_never_a_default_or_zero()
    {
        var runId = await SeedRunAsync();
        var attemptId = await SeedConcludedAsync(runId, 1, limits: null);

        var (status, body) = await GetEvidenceAsync(runId, attemptId);

        Assert.Equal(HttpStatusCode.OK, status);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("modelContextLimits").ValueKind);
        Assert.DoesNotContain("contextWindow", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1200, document.RootElement.GetProperty("tokenUsage").GetProperty("inputTokens").GetInt32());
    }

    [Fact]
    public async Task The_route_still_requires_authentication_and_refuses_a_foreign_run_without_disclosure()
    {
        var runId = await SeedRunAsync();
        var attemptId = await SeedConcludedAsync(runId, 1, Limits(new AgentModelContextLimit("claude-a", 200000, 32000)));
        var otherRunId = await SeedRunAsync();

        using (var anonymous = factory.CreateClient())
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence")).StatusCode);
        }

        using (var wrongSecret = factory.CreateClient())
        {
            wrongSecret.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await wrongSecret.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence")).StatusCode);
        }

        var (foreignStatus, foreignBody) = await GetEvidenceAsync(otherRunId, attemptId);
        Assert.Equal(HttpStatusCode.NotFound, foreignStatus);
        Assert.DoesNotContain("claude-a", foreignBody, StringComparison.Ordinal);
        Assert.DoesNotContain("200000", foreignBody, StringComparison.Ordinal);

        var (unknownStatus, _) = await GetEvidenceAsync(runId, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, unknownStatus);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":2,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":1,\"maxOutputTokens\":1}]}")]
    [InlineData("{\"version\":1,\"source\":\"codex-cli-model-usage-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":1,\"maxOutputTokens\":1}]}")]
    [InlineData("{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"b\",\"contextWindowTokens\":1,\"maxOutputTokens\":1},{\"modelId\":\"a\",\"contextWindowTokens\":1,\"maxOutputTokens\":1}]}")]
    [InlineData("{\"version\":1,\"source\":\"claude-cli-model-usage-v1\",\"models\":[{\"modelId\":\"a\",\"contextWindowTokens\":1,\"maxOutputTokens\":2}]}")]
    public async Task Malformed_persisted_text_is_absent_without_an_error_and_never_disturbs_a_healthy_sibling(string stored)
    {
        var runId = await SeedRunAsync();
        var tamperedId = await SeedConcludedAsync(runId, 1, Limits(new AgentModelContextLimit("claude-a", 200000, 32000)));
        var healthyId = await SeedConcludedAsync(runId, 2, Limits(new AgentModelContextLimit("claude-healthy", 100, 50)));
        await SetStoredAsync(tamperedId, stored);

        var (tamperedStatus, tamperedBody) = await GetEvidenceAsync(runId, tamperedId);
        var (healthyStatus, healthyBody) = await GetEvidenceAsync(runId, healthyId);

        Assert.Equal(HttpStatusCode.OK, tamperedStatus);
        using var tampered = JsonDocument.Parse(tamperedBody);
        Assert.Equal(JsonValueKind.Null, tampered.RootElement.GetProperty("modelContextLimits").ValueKind);
        Assert.True(tampered.RootElement.GetProperty("identityValid").GetBoolean());
        Assert.Equal("ProviderInvocationFailed", tampered.RootElement.GetProperty("outcome").GetString());
        Assert.DoesNotContain("Exception", tamperedBody, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, healthyStatus);
        using var healthy = JsonDocument.Parse(healthyBody);
        var model = Assert.Single(healthy.RootElement.GetProperty("modelContextLimits").GetProperty("models").EnumerateArray());
        Assert.Equal("claude-healthy", model.GetProperty("modelId").GetString());
    }

    [Fact]
    public async Task A_running_attempt_and_an_unprovable_identity_disclose_no_limits_even_beside_a_canonical_snapshot()
    {
        var runId = await SeedRunAsync();
        var runningId = await SeedRunningAsync(runId, 1);
        await SetStoredAsync(runningId, Limits(new AgentModelContextLimit("claude-a", 200000, 32000)).Serialize());
        var incoherentId = await SeedConcludedAsync(runId, 2, Limits(new AgentModelContextLimit("claude-a", 200000, 32000)));
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentRole = {"NotARealRole"} WHERE Id = {incoherentId}");
        }

        var (runningStatus, runningBody) = await GetEvidenceAsync(runId, runningId);
        var (incoherentStatus, incoherentBody) = await GetEvidenceAsync(runId, incoherentId);

        Assert.Equal(HttpStatusCode.OK, runningStatus);
        using (var running = JsonDocument.Parse(runningBody))
        {
            Assert.Equal("Running", running.RootElement.GetProperty("attemptStatus").GetString());
            Assert.Equal(JsonValueKind.Null, running.RootElement.GetProperty("modelContextLimits").ValueKind);
        }

        Assert.Equal(HttpStatusCode.OK, incoherentStatus);
        using var incoherent = JsonDocument.Parse(incoherentBody);
        Assert.False(incoherent.RootElement.GetProperty("identityValid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, incoherent.RootElement.GetProperty("modelContextLimits").ValueKind);
        Assert.DoesNotContain("claude-a", incoherentBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_other_run_or_attempt_response_carries_the_member_or_any_limit()
    {
        var runId = await SeedRunAsync();
        _ = await SeedConcludedAsync(runId, 1, Limits(new AgentModelContextLimit("claude-a", 200000, 32000)));
        using var client = CreateAuthenticatedClient();

        foreach (var path in new[]
                 {
                     $"/api/runs/{runId}/agent-attempts",
                     $"/api/runs/{runId}/cockpit",
                     $"/api/runs/{runId}/agent-attempts/claude-critical-review",
                     $"/api/runs/{runId}/agent-attempts/implementation",
                 })
        {
            var response = await client.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("modelContextLimits", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("contextWindow", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("maxOutputTokens", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("claude-a", body, StringComparison.Ordinal);
        }

    }

    [Fact]
    public void The_evidence_contract_is_additive_the_prior_members_keep_their_names_and_order_and_the_new_one_is_last()
    {
        var members = typeof(AgentAttemptEvidenceResponse).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name!).ToArray();

        Assert.Equal(
            [
                "IdentityValid", "AttemptId", "AttemptNumber", "AttemptStatus", "ClaimedAtUtc", "CompletedAtUtc", "Provider", "Role",
                "ResponseContract", "Outcome", "DispatchedAtUtc", "ProcessExecution", "TokenUsage", "Artifacts", "MaxTurns",
                "RepairSourceAttemptId", "RepairSourceAttemptNumber", "DirectGuidance", "ModelContextLimits",
            ],
            members);
        Assert.Equal(
            ["ModelId", "ContextWindowTokens", "MaxOutputTokens"],
            typeof(AgentModelContextLimitResponse).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(
            ["Models"], typeof(AgentModelContextLimitsResponse).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name));
    }

    [Fact]
    public void The_generated_client_declares_the_additive_nullable_member_only_on_the_attempt_evidence_response()
    {
        var client = File.ReadAllText(GeneratedClientPath());

        var evidenceClass = ClassBlock(client, "AgentAttemptEvidenceResponse");
        Assert.Contains("modelContextLimits?: AgentModelContextLimitsResponse | undefined;", evidenceClass, StringComparison.Ordinal);

        var limitsClass = ClassBlock(client, "AgentModelContextLimitsResponse");
        Assert.Contains("models?: AgentModelContextLimitResponse[];", limitsClass, StringComparison.Ordinal);

        var entryClass = ClassBlock(client, "AgentModelContextLimitResponse");
        Assert.Contains("modelId?: string;", entryClass, StringComparison.Ordinal);
        Assert.Contains("contextWindowTokens?: number;", entryClass, StringComparison.Ordinal);
        Assert.Contains("maxOutputTokens?: number;", entryClass, StringComparison.Ordinal);

        // The member name appears nowhere else: no other response gained it.
        var otherUses = client.Split("modelContextLimits", StringSplitOptions.None).Length - 1;
        var evidenceUses = evidenceClass.Split("modelContextLimits", StringSplitOptions.None).Length - 1;
        var interfaceUses = ClassBlock(client, "IAgentAttemptEvidenceResponse", isInterface: true).Split("modelContextLimits", StringSplitOptions.None).Length - 1;
        Assert.Equal(otherUses, evidenceUses + interfaceUses);
    }

    private static string GeneratedClientPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "frontend", "DevalCopilot.Frontend", "src", "api", "generated", "api-client.ts");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("The generated TypeScript client was not found above the test output directory.");
    }

    private static string ClassBlock(string client, string name, bool isInterface = false)
    {
        var start = client.IndexOf($"export {(isInterface ? "interface" : "class")} {name}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} is not declared by the generated client.");
        var end = client.IndexOf("\nexport ", start + 1, StringComparison.Ordinal);
        return client[start..(end < 0 ? client.Length : end)];
    }

    private async Task<(HttpStatusCode Status, string Body)> GetEvidenceAsync(Guid runId, Guid attemptId)
    {
        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence");
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> SeedRunAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Model limits endpoint", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Expose provider-reported model limits", now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private async Task<Guid> SeedConcludedAsync(Guid runId, int number, AgentModelContextLimitsEvidence? limits)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, number, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, now, number);
        attempt.MarkAgentDispatched(now);
        attempt.CompleteAgent(
            AgentOutcome.ProviderInvocationFailed, null, now.AddSeconds(1),
            AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(2)),
            AgentTokenUsageEvidence.Create(1200, 345, 67, 890, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion), limits);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
        return attempt.Id;
    }

    private async Task<Guid> SeedRunningAsync(Guid runId, int number)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, number, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, now, number);
        attempt.MarkAgentDispatched(now);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
        return attempt.Id;
    }

    private async Task SetStoredAsync(Guid attemptId, string stored)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE attempts SET AgentModelContextLimitsSnapshot = {stored} WHERE Id = {attemptId}");
    }
}
