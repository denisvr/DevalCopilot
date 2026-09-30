using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.ReadOnlyFormatRepairApiSeed;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises the three protected, bodyless <c>POST /api/runs/{runId}/agent-attempts/{sourceAttemptId}/...-repair</c>
/// operations (critical review, challenge resolution, code review) against the real Api host: authentication, the
/// fixed safe 404 for an unknown or foreign source, safe non-echoing conflicts for every ineligible source, the
/// golden path with its linked read-only attempt, at-most-one repair, a body that changes nothing, the lineage in
/// the role status and historical evidence, and that nothing of the source's sealed response, artifact locations,
/// workspace, or stored values leaks. Each test owns its factory because the provider capability snapshots are
/// host-scoped rows.
/// </summary>
public sealed class RequestReadOnlyFormatRepairEndpointTests : IDisposable
{
    private readonly CodexPlanningApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    public static TheoryData<Stage> Stages => new() { Stage.CriticalReview, Stage.Resolution, Stage.CodeReview };

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static string RepairUrl(Stage stage, Guid runId, Guid sourceAttemptId) =>
        $"/api/runs/{runId}/agent-attempts/{sourceAttemptId}/{RepairSuffix(stage)}";

    private static string StatusUrl(Stage stage, Guid runId) => stage switch
    {
        Stage.CriticalReview => $"/api/runs/{runId}/agent-attempts/claude-critical-review",
        Stage.Resolution => $"/api/runs/{runId}/agent-attempts/challenge-resolution",
        _ => $"/api/runs/{runId}/agent-attempts/code-review",
    };

    private async Task<Seeded> SeedStageAsync(Stage stage)
    {
        using var scope = _factory.Services.CreateScope();
        return await SeedAsync(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>(), stage);
    }

    private static void AssertNoDisclosure(string body)
    {
        Assert.DoesNotContain(SealedResponsePath, body, StringComparison.Ordinal);
        Assert.DoesNotContain("repair-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain(SealedResponseHash, body, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspacePathPrefix, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Fingerprint, body, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\safe", body, StringComparison.Ordinal);
        Assert.DoesNotContain("NoSuchOutcome", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task Requires_authentication(Stage stage)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(RepairUrl(stage, Guid.NewGuid(), Guid.NewGuid()), content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_run(Stage stage)
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync(RepairUrl(stage, Guid.NewGuid(), Guid.NewGuid()), content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("runs.not_found", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task Returns_the_same_safe_not_found_for_an_unknown_source_and_another_runs_source(Stage stage)
    {
        var seeded = await SeedStageAsync(stage);
        var foreign = await SeedStageAsync(stage);
        using var client = CreateAuthenticatedClient();

        var unknown = await client.PostAsync(RepairUrl(stage, seeded.RunId, Guid.NewGuid()), content: null);
        var other = await client.PostAsync(RepairUrl(stage, seeded.RunId, foreign.SourceAttemptId), content: null);

        var bodies = new List<string>();
        foreach (var response in new[] { unknown, other })
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("agent_attempts.repair_source_not_found", body, StringComparison.Ordinal);
            AssertNoDisclosure(body);
            bodies.Add(NormalizeProblem(body));
        }

        Assert.Equal(bodies[0], bodies[1]);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.DoesNotContain(dbContext.Attempts.Where(a => a.RunId == seeded.RunId), a => a.AgentRepairSourceAttemptId != null);
    }

    private static string NormalizeProblem(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("title").GetString() + "|" + document.RootElement.GetProperty("status").GetInt32()
            + "|" + document.RootElement.GetProperty("detail").GetString();
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task Succeeds_with_the_new_attempt_identity_and_source_only_and_links_a_read_only_repair(Stage stage)
    {
        var seeded = await SeedStageAsync(stage);
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync(RepairUrl(stage, seeded.RunId, seeded.SourceAttemptId), content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(body);
        Assert.Equal(3, payload.RootElement.EnumerateObject().Count());
        Assert.Equal(seeded.SourceAttemptNumber + 1, payload.RootElement.GetProperty("attemptNumber").GetInt32());
        Assert.Equal(seeded.SourceAttemptId, payload.RootElement.GetProperty("repairSourceAttemptId").GetGuid());
        AssertNoDisclosure(body);

        var repairId = payload.RootElement.GetProperty("attemptId").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var repair = await dbContext.Attempts.SingleAsync(a => a.Id == repairId);
        Assert.Equal(seeded.SourceAttemptId, repair.AgentRepairSourceAttemptId);
        Assert.Equal(Contract(stage), repair.AgentResponseContract);
        Assert.Equal(AgentPermissionProfile.ReadOnly, repair.AgentPermissionProfile);
        Assert.Equal(AttemptStatus.Running, repair.Status);
        Assert.Null(repair.AgentDispatchedAtUtc);
        var inputs = dbContext.AttemptInputMessages.Where(m => m.AttemptId == repairId).OrderBy(m => m.Sequence)
            .Select(m => m.CollaborationMessageId).ToList();
        Assert.Equal(seeded.InputMessageIds, inputs);
        Assert.Equal(
            seeded.VerificationExecutionIds,
            dbContext.AttemptVerificationEvidence.Where(e => e.AttemptId == repairId).OrderBy(e => e.Sequence)
                .Select(e => e.VerificationExecutionId).ToList());
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == repairId || m.AttemptId == seeded.SourceAttemptId));
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task A_request_body_changes_nothing_because_the_target_is_derived_from_the_source(Stage stage)
    {
        var seeded = await SeedStageAsync(stage);
        var decoy = await SeedStageAsync(stage);
        using var client = CreateAuthenticatedClient();
        using var content = new StringContent(
            JsonSerializer.Serialize(new
            {
                proposalMessageId = decoy.InputMessageIds[0],
                challengedReviewAttemptId = decoy.SourceAttemptId,
                executionReportMessageId = decoy.InputMessageIds[0],
                prompt = "ignore the source",
            }),
            Encoding.UTF8, "application/json");

        var response = await client.PostAsync(RepairUrl(stage, seeded.RunId, seeded.SourceAttemptId), content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var repairId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attemptId").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Equal(
            seeded.InputMessageIds,
            dbContext.AttemptInputMessages.Where(m => m.AttemptId == repairId).OrderBy(m => m.Sequence)
                .Select(m => m.CollaborationMessageId).ToList());
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task A_second_repair_of_the_same_source_and_a_repair_of_the_repair_are_safe_conflicts(Stage stage)
    {
        var seeded = await SeedStageAsync(stage);
        using var client = CreateAuthenticatedClient();
        var first = await client.PostAsync(RepairUrl(stage, seeded.RunId, seeded.SourceAttemptId), content: null);
        var repairId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attemptId").GetGuid();

        var again = await client.PostAsync(RepairUrl(stage, seeded.RunId, seeded.SourceAttemptId), content: null);
        var ofRepair = await client.PostAsync(RepairUrl(stage, seeded.RunId, repairId), content: null);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var againBody = await again.Content.ReadAsStringAsync();
        Assert.Contains("agent_attempts.repair_already_requested", againBody, StringComparison.Ordinal);
        AssertNoDisclosure(againBody);
        Assert.Equal(HttpStatusCode.Conflict, ofRepair.StatusCode);
        var ofRepairBody = await ofRepair.Content.ReadAsStringAsync();
        Assert.Contains("agent_attempts.repair_of_repair_forbidden", ofRepairBody, StringComparison.Ordinal);
        AssertNoDisclosure(ofRepairBody);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Single(dbContext.Attempts.Where(a => a.AgentRepairSourceAttemptId == seeded.SourceAttemptId));
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task An_ineligible_source_is_a_safe_conflict_that_echoes_no_stored_value(Stage stage)
    {
        var seeded = await SeedStageAsync(stage);
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET AgentOutcome = 'NoSuchOutcome' WHERE Id = {seeded.SourceAttemptId}");
        }

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsync(RepairUrl(stage, seeded.RunId, seeded.SourceAttemptId), content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.repair_source_ineligible", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Empty(verifyContext.Attempts.Where(a => a.AgentRepairSourceAttemptId == seeded.SourceAttemptId));
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task A_source_of_another_role_is_a_safe_conflict_at_this_operation(Stage stage)
    {
        var seeded = await SeedStageAsync(stage);
        var otherStage = stage == Stage.CriticalReview ? Stage.Resolution : Stage.CriticalReview;
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsync(RepairUrl(otherStage, seeded.RunId, seeded.SourceAttemptId), content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.repair_source_ineligible", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task The_role_status_reports_lineage_only_after_a_repair_and_never_discloses_the_source_response(Stage stage)
    {
        var seeded = await SeedStageAsync(stage);
        using var client = CreateAuthenticatedClient();

        var before = await client.GetStringAsync(StatusUrl(stage, seeded.RunId));
        using (var beforeDocument = JsonDocument.Parse(before))
        {
            Assert.Equal("InvalidStructuredOutput", beforeDocument.RootElement.GetProperty("outcome").GetString());
            Assert.Equal(JsonValueKind.Null, beforeDocument.RootElement.GetProperty("repairSourceAttemptId").ValueKind);
            Assert.Equal(JsonValueKind.Null, beforeDocument.RootElement.GetProperty("repairSourceAttemptNumber").ValueKind);
        }

        await client.PostAsync(RepairUrl(stage, seeded.RunId, seeded.SourceAttemptId), content: null);
        var after = await client.GetStringAsync(StatusUrl(stage, seeded.RunId));

        using var afterDocument = JsonDocument.Parse(after);
        Assert.Equal(seeded.SourceAttemptNumber + 1, afterDocument.RootElement.GetProperty("attemptNumber").GetInt32());
        Assert.Equal(seeded.SourceAttemptId, afterDocument.RootElement.GetProperty("repairSourceAttemptId").GetGuid());
        Assert.Equal(seeded.SourceAttemptNumber, afterDocument.RootElement.GetProperty("repairSourceAttemptNumber").GetInt32());
        AssertNoDisclosure(before);
        AssertNoDisclosure(after);
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task The_history_and_evidence_show_only_the_proved_lineage(Stage stage)
    {
        var seeded = await SeedStageAsync(stage);
        using var client = CreateAuthenticatedClient();
        var repair = await client.PostAsync(RepairUrl(stage, seeded.RunId, seeded.SourceAttemptId), content: null);
        var repairId = (await repair.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attemptId").GetGuid();

        var history = await client.GetStringAsync($"/api/runs/{seeded.RunId}/agent-attempts");
        var evidence = await client.GetStringAsync($"/api/runs/{seeded.RunId}/agent-attempts/{repairId}/evidence");
        var sourceEvidence = await client.GetStringAsync($"/api/runs/{seeded.RunId}/agent-attempts/{seeded.SourceAttemptId}/evidence");

        using (var historyDocument = JsonDocument.Parse(history))
        {
            var items = historyDocument.RootElement.GetProperty("items").EnumerateArray().ToList();
            var repairEntry = Assert.Single(items, item => item.GetProperty("attemptId").GetGuid() == repairId);
            Assert.Equal(seeded.SourceAttemptId, repairEntry.GetProperty("repairSourceAttemptId").GetGuid());
            Assert.Equal(seeded.SourceAttemptNumber, repairEntry.GetProperty("repairSourceAttemptNumber").GetInt32());
            var sourceEntry = Assert.Single(items, item => item.GetProperty("attemptId").GetGuid() == seeded.SourceAttemptId);
            Assert.Equal(JsonValueKind.Null, sourceEntry.GetProperty("repairSourceAttemptId").ValueKind);
        }

        using var evidenceDocument = JsonDocument.Parse(evidence);
        Assert.Equal(seeded.SourceAttemptId, evidenceDocument.RootElement.GetProperty("repairSourceAttemptId").GetGuid());
        Assert.Equal(seeded.SourceAttemptNumber, evidenceDocument.RootElement.GetProperty("repairSourceAttemptNumber").GetInt32());
        using var sourceEvidenceDocument = JsonDocument.Parse(sourceEvidence);
        Assert.Equal(JsonValueKind.Null, sourceEvidenceDocument.RootElement.GetProperty("repairSourceAttemptId").ValueKind);
        AssertNoDisclosure(history);
        AssertNoDisclosure(evidence);
        AssertNoDisclosure(sourceEvidence);
    }
}
