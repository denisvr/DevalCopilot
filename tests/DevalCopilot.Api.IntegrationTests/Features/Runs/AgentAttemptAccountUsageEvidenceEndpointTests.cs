using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>The historical evidence of the run-scoped Codex account-usage stop (ADR-0025), exposed only by the existing attempt-evidence
/// route through the real authenticated MVC pipeline: the attempt's own threshold snapshot and the recorded pre-dispatch decision, read
/// from stored facts alone. A decision whose stored text cannot be proved is an unavailable fact without a failure; an attempt that was
/// not stopped has no decision (never a guessed below-threshold one); nothing carries provider text, an account, a path or a raw payload.</summary>
public sealed class AgentAttemptAccountUsageEvidenceEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Fingerprint = new('a', 64);

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<Guid> SeedRunAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Account usage evidence", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Show the account-usage decision", now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private async Task<Guid> SeedCodexAttemptAsync(Guid runId, int number, int? snapshot, Action<Attempt>? conclude = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, number, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, DateTimeOffset.UtcNow, number);
        if (snapshot is { } threshold)
        {
            attempt.SnapshotCodexAccountUsageStop(threshold);
        }

        conclude?.Invoke(attempt);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync();
        return attempt.Id;
    }

    private async Task<JsonElement> GetEvidenceAsync(Guid runId, Guid attemptId)
    {
        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attemptId}/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static AgentCodexAccountUsageDecision Reached(DateTimeOffset at) => AgentCodexAccountUsageDecision.Create(
        CodexAccountUsageDecisionKind.Reached, CodexAccountUsageDecisionReason.ThresholdReached, 80, at,
        [new("codex", CodexAccountUsageWindowKind.Secondary, 12), new("codex", CodexAccountUsageWindowKind.Primary, 85)]);

    [Fact]
    public async Task A_stopped_attempt_exposes_its_threshold_and_the_validated_decision_in_ordinal_order_and_nothing_else()
    {
        var runId = await SeedRunAsync();
        var at = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var attemptId = await SeedCodexAttemptAsync(runId, 1, 80, attempt => attempt.CompleteAgentAccountUsageStop(Reached(at), at.AddSeconds(1)));

        var evidence = await GetEvidenceAsync(runId, attemptId);

        Assert.Equal("AccountUsageStopReached", evidence.GetProperty("outcome").GetString());
        Assert.Equal("Failed", evidence.GetProperty("attemptStatus").GetString());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("dispatchedAtUtc").ValueKind);
        var stop = evidence.GetProperty("accountUsageStop");
        Assert.Equal("Configured", stop.GetProperty("state").GetString());
        Assert.Equal(80, stop.GetProperty("percent").GetInt32());
        var decision = evidence.GetProperty("accountUsageDecision");
        Assert.Equal(["state", "decision", "reason", "thresholdPercent", "retrievedAtUtc", "windows"], decision.EnumerateObject().Select(member => member.Name));
        Assert.Equal("Recorded", decision.GetProperty("state").GetString());
        Assert.Equal("Reached", decision.GetProperty("decision").GetString());
        Assert.Equal("ThresholdReached", decision.GetProperty("reason").GetString());
        Assert.Equal(80, decision.GetProperty("thresholdPercent").GetInt32());
        Assert.Equal(at, decision.GetProperty("retrievedAtUtc").GetDateTimeOffset());
        var windows = decision.GetProperty("windows").EnumerateArray().ToArray();
        Assert.Equal(["Primary", "Secondary"], windows.Select(window => window.GetProperty("window").GetString()));
        Assert.Equal([85, 12], windows.Select(window => window.GetProperty("usedPercent").GetInt32()));
        Assert.All(windows, window => Assert.Equal("codex", window.GetProperty("bucketId").GetString()));
        var text = evidence.GetRawText();
        Assert.DoesNotContain("codex-account-rate-limits", text, StringComparison.Ordinal);
        Assert.DoesNotContain("version", decision.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unavailable_decision_reports_its_fixed_reason_without_windows()
    {
        var runId = await SeedRunAsync();
        var decision = AgentCodexAccountUsageDecision.Create(
            CodexAccountUsageDecisionKind.Unavailable, CodexAccountUsageDecisionReason.EvidenceExpired, 60, DateTimeOffset.UtcNow.AddMinutes(-1), []);
        var attemptId = await SeedCodexAttemptAsync(runId, 1, 60, attempt => attempt.CompleteAgentAccountUsageStop(decision, DateTimeOffset.UtcNow));

        var evidence = await GetEvidenceAsync(runId, attemptId);

        Assert.Equal("AccountUsageEvidenceUnavailable", evidence.GetProperty("outcome").GetString());
        var shown = evidence.GetProperty("accountUsageDecision");
        Assert.Equal("Unavailable", shown.GetProperty("decision").GetString());
        Assert.Equal("EvidenceExpired", shown.GetProperty("reason").GetString());
        Assert.Empty(shown.GetProperty("windows").EnumerateArray());
    }

    [Fact]
    public async Task A_tampered_decision_text_is_an_unavailable_fact_and_never_a_failure()
    {
        var runId = await SeedRunAsync();
        var attemptId = await SeedCodexAttemptAsync(runId, 1, 80, attempt => attempt.CompleteAgentAccountUsageStop(Reached(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow));
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentAccountUsageDecisionSnapshot = {"{\"version\":9}"} WHERE Id = {attemptId}");
        }

        var evidence = await GetEvidenceAsync(runId, attemptId);

        var decision = evidence.GetProperty("accountUsageDecision");
        Assert.Equal("Unavailable", decision.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, decision.GetProperty("decision").ValueKind);
        Assert.Equal(JsonValueKind.Null, decision.GetProperty("reason").ValueKind);
        Assert.Empty(decision.GetProperty("windows").EnumerateArray());
        Assert.Equal("AccountUsageStopReached", evidence.GetProperty("outcome").GetString());
    }

    [Theory]
    [InlineData("decision-threshold-50")]
    [InlineData("snapshot-absent")]
    [InlineData("snapshot-changed")]
    [InlineData("snapshot-malformed")]
    public async Task A_canonical_decision_that_contradicts_the_attempts_own_snapshot_is_an_unavailable_fact_that_exposes_nothing_of_it(string tamper)
    {
        var runId = await SeedRunAsync();
        var attemptId = await SeedCodexAttemptAsync(runId, 1, 80, attempt => attempt.CompleteAgentAccountUsageStop(Reached(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow));
        string before;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            switch (tamper)
            {
                case "decision-threshold-50":
                    await dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE attempts SET AgentAccountUsageDecisionSnapshot = replace(AgentAccountUsageDecisionSnapshot, {"\"thresholdPercent\":80"}, {"\"thresholdPercent\":50"}) WHERE Id = {attemptId}");
                    break;
                case "snapshot-absent":
                    await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentCodexAccountUsageStopPercent = NULL WHERE Id = {attemptId}");
                    break;
                case "snapshot-changed":
                    await dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE attempts SET AgentCodexAccountUsageStopPercent = {CodexAccountUsageStop.Format(90)} WHERE Id = {attemptId}");
                    break;
                default:
                    await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentCodexAccountUsageStopPercent = {"t:80"} WHERE Id = {attemptId}");
                    break;
            }

            before = await dbContext.Database.SqlQuery<string>($"SELECT AgentAccountUsageDecisionSnapshot AS Value FROM attempts WHERE Id = {attemptId}").SingleAsync();
        }

        var evidence = await GetEvidenceAsync(runId, attemptId);

        var decision = evidence.GetProperty("accountUsageDecision");
        Assert.Equal("Unavailable", decision.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, decision.GetProperty("decision").ValueKind);
        Assert.Equal(JsonValueKind.Null, decision.GetProperty("reason").ValueKind);
        Assert.Equal(JsonValueKind.Null, decision.GetProperty("thresholdPercent").ValueKind);
        Assert.Equal(JsonValueKind.Null, decision.GetProperty("retrievedAtUtc").ValueKind);
        Assert.Empty(decision.GetProperty("windows").EnumerateArray());
        Assert.Equal("AccountUsageStopReached", evidence.GetProperty("outcome").GetString());
        using var after = factory.Services.CreateScope();
        Assert.Equal(before, await after.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().Database
            .SqlQuery<string>($"SELECT AgentAccountUsageDecisionSnapshot AS Value FROM attempts WHERE Id = {attemptId}").SingleAsync());
    }

    [Fact]
    public async Task A_codex_attempt_claimed_with_the_stop_disabled_or_before_the_setting_has_no_policy_and_no_decision()
    {
        var runId = await SeedRunAsync();
        var disabled = await SeedCodexAttemptAsync(runId, 1, null, attempt =>
        {
            attempt.MarkAgentDispatched(DateTimeOffset.UtcNow);
            attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, DateTimeOffset.UtcNow);
        });

        var evidence = await GetEvidenceAsync(runId, disabled);

        Assert.Equal("NotConfigured", evidence.GetProperty("accountUsageStop").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("accountUsageStop").GetProperty("percent").ValueKind);
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("accountUsageDecision").ValueKind);
    }

    [Fact]
    public async Task A_dispatched_attempt_that_snapshotted_a_threshold_has_no_decision_never_a_guessed_below_threshold_one()
    {
        var runId = await SeedRunAsync();
        var attemptId = await SeedCodexAttemptAsync(runId, 1, 80, attempt =>
        {
            attempt.MarkAgentDispatched(DateTimeOffset.UtcNow);
            attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, DateTimeOffset.UtcNow);
        });

        var evidence = await GetEvidenceAsync(runId, attemptId);

        Assert.Equal(80, evidence.GetProperty("accountUsageStop").GetProperty("percent").GetInt32());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("accountUsageDecision").ValueKind);
    }

    [Fact]
    public async Task A_malformed_threshold_snapshot_is_unknown_without_a_number_and_the_route_still_answers()
    {
        var runId = await SeedRunAsync();
        var attemptId = await SeedCodexAttemptAsync(runId, 1, 80);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentCodexAccountUsageStopPercent = {3.5} WHERE Id = {attemptId}");
        }

        var evidence = await GetEvidenceAsync(runId, attemptId);

        Assert.Equal("Unknown", evidence.GetProperty("accountUsageStop").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("accountUsageStop").GetProperty("percent").ValueKind);
    }

    [Fact]
    public async Task A_claude_attempt_carries_no_account_usage_members_and_other_routes_never_expose_them()
    {
        var runId = await SeedRunAsync();
        Guid attemptId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var attempt = Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 65536, 131072, DateTimeOffset.UtcNow, 1);
            attemptId = attempt.Id;
            dbContext.Attempts.Add(attempt);
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0));
            await dbContext.SaveChangesAsync();
        }

        var evidence = await GetEvidenceAsync(runId, attemptId);

        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("accountUsageStop").ValueKind);
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("accountUsageDecision").ValueKind);
        using var client = CreateAuthenticatedClient();
        var history = await client.GetStringAsync($"/api/runs/{runId}/agent-attempts");
        Assert.DoesNotContain("accountUsage", history, StringComparison.OrdinalIgnoreCase);
    }
}
