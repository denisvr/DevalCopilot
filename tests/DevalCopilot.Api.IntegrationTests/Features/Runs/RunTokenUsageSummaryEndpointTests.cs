using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises the run-wide, read-only provider-reported token-usage summary exposed by the run
/// cockpit through the real MVC pipeline: a still-running dispatched attempt is always pending, never
/// counted as known usage, and is kept distinct from a terminal attempt that genuinely lacks a
/// trusted usage contract — scoped strictly to the requested run.
/// </summary>
public sealed class RunTokenUsageSummaryEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Fingerprint = new('a', 64);

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<JsonElement> GetTokenUsageSummaryAsync(Guid runId)
    {
        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/cockpit");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("tokenUsageSummary").Clone();
    }

    private async Task<Guid> SeedRunAsync(string projectName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), projectName, $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Expose the token-usage summary", now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private static Attempt ClaimAndDispatch(Guid runId, int attemptNumber, DateTimeOffset now)
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, now, attemptNumber);
        attempt.MarkAgentDispatched(now);
        return attempt;
    }

    /// <summary>Dispatched, terminal, with known provider-reported usage.</summary>
    private async Task AddCompletedAttemptWithUsageAsync(Guid runId, int attemptNumber, int inputTokens, int outputTokens)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attempt = ClaimAndDispatch(runId, attemptNumber, now);
        // ClaimAgent fixes AgentProvider to Codex, so evidence must use the matching proven schema —
        // a mismatched provider/schema pair would itself project as unknown usage (see
        // AgentTokenUsageEvidencePolicy.IsSupportedSource), which is not the scenario under test here.
        var usage = AgentTokenUsageEvidence.Create(inputTokens, outputTokens, null, null, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion);
        var processEvidence = AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromSeconds(1));
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now.AddSeconds(1), processEvidence, usage);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Dispatched, terminal, but with no usage evidence recorded — the truthful "provider
    /// reported nothing trustworthy" case, never silently zero.</summary>
    private async Task AddCompletedAttemptWithoutUsageAsync(Guid runId, int attemptNumber)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attempt = ClaimAndDispatch(runId, attemptNumber, now);
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, now.AddSeconds(1), processEvidence: null, tokenUsage: null);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Dispatched but still running — no terminal result yet, so any usage is never
    /// trusted.</summary>
    private async Task AddPendingDispatchedAttemptAsync(Guid runId, int attemptNumber)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attempt = ClaimAndDispatch(runId, attemptNumber, now);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task A_run_without_any_dispatched_agent_attempt_reports_no_dispatched_attempts()
    {
        var runId = await SeedRunAsync("No dispatched attempts");

        var summary = await GetTokenUsageSummaryAsync(runId);

        Assert.Equal("NoDispatchedAttempts", summary.GetProperty("completeness").GetString());
        Assert.Equal(0, summary.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(0, summary.GetProperty("attemptsWithUnknownUsage").GetInt32());
        Assert.Equal(0, summary.GetProperty("pendingAttemptCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("terminalAttemptsWithUnknownUsage").GetInt32());
    }

    [Fact]
    public async Task A_run_with_only_known_terminal_usage_reports_complete_with_the_exact_total()
    {
        var runId = await SeedRunAsync("Complete usage");
        await AddCompletedAttemptWithUsageAsync(runId, 1, 1000, 200);
        await AddCompletedAttemptWithUsageAsync(runId, 2, 500, 50);

        var summary = await GetTokenUsageSummaryAsync(runId);

        Assert.Equal("Complete", summary.GetProperty("completeness").GetString());
        Assert.Equal(2, summary.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(0, summary.GetProperty("attemptsWithUnknownUsage").GetInt32());
        Assert.Equal(0, summary.GetProperty("pendingAttemptCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("terminalAttemptsWithUnknownUsage").GetInt32());
        Assert.Equal(1500, summary.GetProperty("inputTokens").GetInt64());
        Assert.Equal(250, summary.GetProperty("outputTokens").GetInt64());
    }

    [Fact]
    public async Task A_run_with_only_pending_dispatched_attempts_reports_pending_evidence()
    {
        // Only one Running Agent attempt is ever allowed per run (see the filtered unique index
        // backing the one-Running-attempt-per-run invariant), so the pending-only scenario is
        // necessarily a single dispatched attempt still running.
        var runId = await SeedRunAsync("Pending only");
        await AddPendingDispatchedAttemptAsync(runId, 1);

        var summary = await GetTokenUsageSummaryAsync(runId);

        Assert.Equal("PendingEvidence", summary.GetProperty("completeness").GetString());
        Assert.Equal(0, summary.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(1, summary.GetProperty("attemptsWithUnknownUsage").GetInt32());
        Assert.Equal(1, summary.GetProperty("pendingAttemptCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("terminalAttemptsWithUnknownUsage").GetInt32());
    }

    [Fact]
    public async Task A_run_with_only_terminal_unknown_usage_reports_partial_with_zero_sums()
    {
        var runId = await SeedRunAsync("Terminal unknown only");
        await AddCompletedAttemptWithoutUsageAsync(runId, 1);
        await AddCompletedAttemptWithoutUsageAsync(runId, 2);

        var summary = await GetTokenUsageSummaryAsync(runId);

        Assert.Equal("Partial", summary.GetProperty("completeness").GetString());
        Assert.Equal(0, summary.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(2, summary.GetProperty("attemptsWithUnknownUsage").GetInt32());
        Assert.Equal(0, summary.GetProperty("pendingAttemptCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("terminalAttemptsWithUnknownUsage").GetInt32());
        Assert.Equal(0, summary.GetProperty("inputTokens").GetInt64());
    }

    [Fact]
    public async Task A_run_mixing_known_pending_and_terminal_unknown_usage_reports_partial()
    {
        var runId = await SeedRunAsync("Mixed usage");
        await AddCompletedAttemptWithUsageAsync(runId, 1, 1000, 200);
        await AddCompletedAttemptWithoutUsageAsync(runId, 2);
        await AddPendingDispatchedAttemptAsync(runId, 3);

        var summary = await GetTokenUsageSummaryAsync(runId);

        Assert.Equal("Partial", summary.GetProperty("completeness").GetString());
        Assert.Equal(1, summary.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(2, summary.GetProperty("attemptsWithUnknownUsage").GetInt32());
        Assert.Equal(1, summary.GetProperty("pendingAttemptCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("terminalAttemptsWithUnknownUsage").GetInt32());
        Assert.Equal(1000, summary.GetProperty("inputTokens").GetInt64());
        Assert.Equal(200, summary.GetProperty("outputTokens").GetInt64());
    }

    [Fact]
    public async Task A_second_runs_dispatched_attempts_never_leak_into_the_first_runs_summary()
    {
        var firstRunId = await SeedRunAsync("Isolation first run");
        var secondRunId = await SeedRunAsync("Isolation second run");

        await AddCompletedAttemptWithUsageAsync(firstRunId, 1, 100, 10);
        await AddCompletedAttemptWithUsageAsync(secondRunId, 1, 900, 90);
        await AddCompletedAttemptWithoutUsageAsync(secondRunId, 2);

        var firstSummary = await GetTokenUsageSummaryAsync(firstRunId);
        var secondSummary = await GetTokenUsageSummaryAsync(secondRunId);

        Assert.Equal("Complete", firstSummary.GetProperty("completeness").GetString());
        Assert.Equal(100, firstSummary.GetProperty("inputTokens").GetInt64());
        Assert.Equal(1, firstSummary.GetProperty("attemptsWithKnownUsage").GetInt32());

        Assert.Equal("Partial", secondSummary.GetProperty("completeness").GetString());
        Assert.Equal(1, secondSummary.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(1, secondSummary.GetProperty("terminalAttemptsWithUnknownUsage").GetInt32());
    }
}
