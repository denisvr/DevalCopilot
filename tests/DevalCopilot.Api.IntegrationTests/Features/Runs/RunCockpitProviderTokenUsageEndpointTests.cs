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
/// Exercises the cockpit's provider-separated, read-only token-usage projection through the real MVC
/// pipeline. It lives alongside — and never changes — the existing run-wide
/// <c>tokenUsageSummary</c> exposed by <see cref="RunTokenUsageSummaryEndpointTests"/>.
/// </summary>
public sealed class RunCockpitProviderTokenUsageEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Fingerprint = new('a', 64);

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<JsonElement> GetCockpitAsync(Guid runId)
    {
        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/runs/{runId}/cockpit");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private async Task<Guid> SeedRunAsync(string projectName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), projectName, $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Expose the provider token-usage projection", now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    /// <summary>Dispatched, terminal, Codex-attributed, with known provider-reported usage.</summary>
    private async Task AddCompletedCodexAttemptAsync(Guid runId, int attemptNumber, int inputTokens, int outputTokens)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, now, attemptNumber);
        attempt.MarkAgentDispatched(now);
        var usage = AgentTokenUsageEvidence.Create(inputTokens, outputTokens, null, null, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion);
        var processEvidence = AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromSeconds(1));
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now.AddSeconds(1), processEvidence, usage);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Dispatched, terminal, Claude-Code-attributed, with known provider-reported usage.</summary>
    private async Task AddCompletedClaudeCodeAttemptAsync(
        Guid runId, int attemptNumber, int inputTokens, int outputTokens, int cacheCreationInputTokens, int cacheReadInputTokens)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 65536, 131072, now, attemptNumber);
        attempt.MarkAgentDispatched(now);
        var usage = AgentTokenUsageEvidence.Create(
            inputTokens, outputTokens, cacheCreationInputTokens, cacheReadInputTokens, AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion);
        var processEvidence = AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromSeconds(1));
        attempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, now.AddSeconds(1), processEvidence, usage);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
    }

    private static JsonElement SummaryFor(JsonElement providerTokenUsageSummaries, string attribution) =>
        providerTokenUsageSummaries.EnumerateArray()
            .Single(entry => entry.GetProperty("attribution").GetString() == attribution)
            .GetProperty("summary");

    [Fact]
    public async Task A_run_without_any_dispatched_agent_attempt_reports_all_three_buckets_as_empty()
    {
        var runId = await SeedRunAsync("No dispatched attempts");

        var cockpit = await GetCockpitAsync(runId);
        var entries = cockpit.GetProperty("providerTokenUsageSummaries");

        Assert.Equal(3, entries.GetArrayLength());
        Assert.Equal(["Codex", "ClaudeCode", "Unattributed"], entries.EnumerateArray().Select(e => e.GetProperty("attribution").GetString()));
        foreach (var attribution in new[] { "Codex", "ClaudeCode", "Unattributed" })
        {
            Assert.Equal("NoDispatchedAttempts", SummaryFor(entries, attribution).GetProperty("completeness").GetString());
        }
    }

    [Fact]
    public async Task Codex_and_ClaudeCode_attempts_are_reported_in_separate_buckets_while_the_run_wide_total_still_covers_both()
    {
        var runId = await SeedRunAsync("Separate provider buckets");
        await AddCompletedCodexAttemptAsync(runId, 1, 1000, 200);
        await AddCompletedClaudeCodeAttemptAsync(runId, 2, 500, 50, 5, 60);

        var cockpit = await GetCockpitAsync(runId);

        var runWide = cockpit.GetProperty("tokenUsageSummary");
        Assert.Equal("Complete", runWide.GetProperty("completeness").GetString());
        Assert.Equal(2, runWide.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(1500, runWide.GetProperty("inputTokens").GetInt64());
        Assert.Equal(250, runWide.GetProperty("outputTokens").GetInt64());

        var entries = cockpit.GetProperty("providerTokenUsageSummaries");
        var codex = SummaryFor(entries, "Codex");
        Assert.Equal("Complete", codex.GetProperty("completeness").GetString());
        Assert.Equal(1, codex.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(1000, codex.GetProperty("inputTokens").GetInt64());
        Assert.Equal(200, codex.GetProperty("outputTokens").GetInt64());

        var claude = SummaryFor(entries, "ClaudeCode");
        Assert.Equal("Complete", claude.GetProperty("completeness").GetString());
        Assert.Equal(1, claude.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(500, claude.GetProperty("inputTokens").GetInt64());
        Assert.Equal(50, claude.GetProperty("outputTokens").GetInt64());
        Assert.Equal(5, claude.GetProperty("cacheCreationInputTokens").GetInt64());
        Assert.Equal(60, claude.GetProperty("cacheReadInputTokens").GetInt64());

        var unattributed = SummaryFor(entries, "Unattributed");
        Assert.Equal("NoDispatchedAttempts", unattributed.GetProperty("completeness").GetString());
    }

    [Fact]
    public async Task A_second_runs_provider_buckets_never_leak_into_the_first_runs_buckets()
    {
        var firstRunId = await SeedRunAsync("Isolation first run");
        var secondRunId = await SeedRunAsync("Isolation second run");

        await AddCompletedCodexAttemptAsync(firstRunId, 1, 100, 10);
        await AddCompletedClaudeCodeAttemptAsync(secondRunId, 1, 900, 90, 1, 2);

        var firstEntries = (await GetCockpitAsync(firstRunId)).GetProperty("providerTokenUsageSummaries");
        var secondEntries = (await GetCockpitAsync(secondRunId)).GetProperty("providerTokenUsageSummaries");

        Assert.Equal("Complete", SummaryFor(firstEntries, "Codex").GetProperty("completeness").GetString());
        Assert.Equal("NoDispatchedAttempts", SummaryFor(firstEntries, "ClaudeCode").GetProperty("completeness").GetString());

        Assert.Equal("NoDispatchedAttempts", SummaryFor(secondEntries, "Codex").GetProperty("completeness").GetString());
        Assert.Equal("Complete", SummaryFor(secondEntries, "ClaudeCode").GetProperty("completeness").GetString());
    }
}
