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

/// <summary>
/// Exercises the provider-reported token usage exposed by the Agent status endpoints and the run
/// cockpit through the real MVC pipeline: per-attempt usage is a bounded sibling of the semantic
/// outcome and the process evidence, absent usage is explicit, the internal schema version never
/// reaches a response, and the run-level summary distinguishes Complete, Partial, and
/// NoDispatchedAttempts so a partial sum is never presented as the run's total. Background
/// supervisors are removed by the factory, so every attempt below is seeded before a request.
/// </summary>
public sealed class AgentTokenUsageEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private const string SessionSentinel = "provider-session-sentinel-must-not-leak";
    private const string SchemaVersionSentinel = AgentTokenUsageEvidencePolicy.ClaudeCliSchemaVersion;
    private static readonly string Fingerprint = new('a', 64);
    private static readonly HashSet<string> UsageFields = ["inputTokens", "outputTokens", "cacheCreationInputTokens", "cacheReadInputTokens"];
    private static readonly HashSet<string> SummaryFields =
    [
        "completeness", "attemptsWithKnownUsage", "attemptsWithUnknownUsage", "inputTokens", "outputTokens",
        "cacheCreationInputTokens", "cacheReadInputTokens",
    ];

    private static readonly AgentTokenUsageEvidence Usage = AgentTokenUsageEvidence.Create(1200, 345, 67, 890, SchemaVersionSentinel);
    private static readonly AgentTokenUsageEvidence OtherUsage = AgentTokenUsageEvidence.Create(800, 55, 3, 110, SchemaVersionSentinel);

    private static readonly AgentTokenUsageEvidence CodexUsage =
        AgentTokenUsageEvidence.Create(2400, 120, null, null, AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion);

    public static TheoryData<string, AgentResponseContract> ClaudeRoleRoutes => new()
    {
        { "agent-attempts/claude-critical-review", AgentResponseContract.CriticalReview },
        { "agent-attempts/implementation", AgentResponseContract.ImplementationReport },
    };

    public static TheoryData<string, AgentResponseContract> CodexRoleRoutes => new()
    {
        { "agent-attempts/codex-plan", AgentResponseContract.Proposal },
        { "agent-attempts/challenge-resolution", AgentResponseContract.ChallengeResolution },
        { "agent-attempts/code-review", AgentResponseContract.ImplementationReview },
    };

    [Theory]
    [MemberData(nameof(ClaudeRoleRoutes))]
    public async Task Each_claude_role_status_exposes_usage_as_a_bounded_sibling_of_the_outcome_and_process_evidence(
        string route, AgentResponseContract contract)
    {
        var runId = await SeedRunAsync();
        await AddAttemptAsync(runId, 1, contract, AgentOutcome.ProviderInvocationFailed, Usage);

        var body = await GetOkBodyAsync($"/api/runs/{runId}/{route}");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal("ProviderInvocationFailed", root.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("processExecution").ValueKind);
        var usage = AssertBoundedShape(root.GetProperty("tokenUsage"), UsageFields);
        Assert.Equal(1200, usage.GetProperty("inputTokens").GetInt32());
        Assert.Equal(345, usage.GetProperty("outputTokens").GetInt32());
        Assert.Equal(67, usage.GetProperty("cacheCreationInputTokens").GetInt32());
        Assert.Equal(890, usage.GetProperty("cacheReadInputTokens").GetInt32());
        AssertNoDisclosure(body);
    }

    [Theory]
    [MemberData(nameof(CodexRoleRoutes))]
    public async Task Codex_role_status_exposes_null_usage_when_none_was_reported(
        string route, AgentResponseContract contract)
    {
        var runId = await SeedRunAsync();
        await AddAttemptAsync(runId, 1, contract, AgentOutcome.ProviderInvocationFailed, usage: null);

        var body = await GetOkBodyAsync($"/api/runs/{runId}/{route}");
        using var document = JsonDocument.Parse(body);
        var usage = AssertBoundedShape(document.RootElement.GetProperty("tokenUsage"), UsageFields);
        Assert.All(UsageFields, field => Assert.Equal(JsonValueKind.Null, usage.GetProperty(field).ValueKind));
        AssertNoDisclosure(body);
    }

    // Codex now has its own proven usage contract (see CodexCliTokenUsage), distinct from Claude's:
    // input/output counts are known, but the cache members stay null because Codex's single
    // "served from cache" count has no proven correspondence to Claude's cache-creation/cache-read
    // breakdown.
    [Theory]
    [MemberData(nameof(CodexRoleRoutes))]
    public async Task Each_codex_role_status_exposes_its_own_proven_usage_as_a_bounded_sibling_of_the_outcome_and_process_evidence(
        string route, AgentResponseContract contract)
    {
        var runId = await SeedRunAsync();
        await AddAttemptAsync(runId, 1, contract, AgentOutcome.ProviderInvocationFailed, CodexUsage);

        var body = await GetOkBodyAsync($"/api/runs/{runId}/{route}");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal("ProviderInvocationFailed", root.GetProperty("outcome").GetString());
        var usage = AssertBoundedShape(root.GetProperty("tokenUsage"), UsageFields);
        Assert.Equal(2400, usage.GetProperty("inputTokens").GetInt32());
        Assert.Equal(120, usage.GetProperty("outputTokens").GetInt32());
        Assert.Equal(JsonValueKind.Null, usage.GetProperty("cacheCreationInputTokens").ValueKind);
        Assert.Equal(JsonValueKind.Null, usage.GetProperty("cacheReadInputTokens").ValueKind);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Absent_usage_is_explicit_with_every_member_null()
    {
        var runId = await SeedRunAsync();
        await AddAttemptAsync(runId, 1, AgentResponseContract.Proposal, AgentOutcome.ProviderInvocationFailed, usage: null);

        var body = await GetOkBodyAsync($"/api/runs/{runId}/agent-attempts/codex-plan");
        using var document = JsonDocument.Parse(body);
        var usage = AssertBoundedShape(document.RootElement.GetProperty("tokenUsage"), UsageFields);

        foreach (var field in UsageFields)
        {
            Assert.Equal(JsonValueKind.Null, usage.GetProperty(field).ValueKind);
        }

        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task A_run_without_an_attempt_has_no_token_usage_object_at_all()
    {
        var runId = await SeedRunAsync();

        var body = await GetOkBodyAsync($"/api/runs/{runId}/agent-attempts/claude-critical-review");
        using var document = JsonDocument.Parse(body);

        Assert.False(document.RootElement.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("tokenUsage").ValueKind);
    }

    [Fact]
    public async Task The_cockpit_reports_a_complete_total_when_every_dispatched_attempt_has_known_usage()
    {
        var runId = await SeedRunAsync();
        await AddAttemptAsync(runId, 1, AgentResponseContract.CriticalReview, AgentOutcome.ProviderInvocationFailed, Usage);
        await AddAttemptAsync(runId, 2, AgentResponseContract.ImplementationReport, AgentOutcome.ProviderInvocationFailed, OtherUsage);

        var body = await GetOkBodyAsync($"/api/runs/{runId}/cockpit");
        using var document = JsonDocument.Parse(body);
        var summary = AssertBoundedShape(document.RootElement.GetProperty("tokenUsageSummary"), SummaryFields);

        Assert.Equal("Complete", summary.GetProperty("completeness").GetString());
        Assert.Equal(2, summary.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(0, summary.GetProperty("attemptsWithUnknownUsage").GetInt32());
        Assert.Equal(2000, summary.GetProperty("inputTokens").GetInt64());
        Assert.Equal(400, summary.GetProperty("outputTokens").GetInt64());
        Assert.Equal(70, summary.GetProperty("cacheCreationInputTokens").GetInt64());
        Assert.Equal(1000, summary.GetProperty("cacheReadInputTokens").GetInt64());

        var latestUsage = AssertBoundedShape(document.RootElement.GetProperty("latestAgentAttempt").GetProperty("tokenUsage"), UsageFields);
        Assert.Equal(800, latestUsage.GetProperty("inputTokens").GetInt32());
        AssertNoDisclosure(body);
    }

    // The ordinary case of an attempt whose provider genuinely reported nothing: its usage is
    // unknown and the summary is Partial — its sum covers only the known Claude attempt.
    [Fact]
    public async Task The_cockpit_reports_a_partial_sum_when_any_dispatched_attempt_lacks_usage()
    {
        var runId = await SeedRunAsync();
        await AddAttemptAsync(runId, 1, AgentResponseContract.Proposal, AgentOutcome.ProviderInvocationFailed, usage: null);
        await AddAttemptAsync(runId, 2, AgentResponseContract.CriticalReview, AgentOutcome.ProviderInvocationFailed, Usage);

        var body = await GetOkBodyAsync($"/api/runs/{runId}/cockpit");
        using var document = JsonDocument.Parse(body);
        var summary = AssertBoundedShape(document.RootElement.GetProperty("tokenUsageSummary"), SummaryFields);

        Assert.Equal("Partial", summary.GetProperty("completeness").GetString());
        Assert.Equal(1, summary.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(1, summary.GetProperty("attemptsWithUnknownUsage").GetInt32());
        Assert.Equal(1200, summary.GetProperty("inputTokens").GetInt64());
        Assert.Equal(345, summary.GetProperty("outputTokens").GetInt64());
        AssertNoDisclosure(body);
    }

    // The same run reported Partial before either attempt had known usage (the prior test above);
    // once both providers report their own proven usage, completeness flips to Complete, and the
    // sum now includes the Codex attempt's input/output counts with no cache contribution.
    [Fact]
    public async Task The_cockpit_reports_a_complete_total_when_a_codex_attempt_reports_its_own_proven_usage_alongside_a_claude_attempt()
    {
        var runId = await SeedRunAsync();
        await AddAttemptAsync(runId, 1, AgentResponseContract.Proposal, AgentOutcome.ProviderInvocationFailed, CodexUsage);
        await AddAttemptAsync(runId, 2, AgentResponseContract.CriticalReview, AgentOutcome.ProviderInvocationFailed, Usage);

        var body = await GetOkBodyAsync($"/api/runs/{runId}/cockpit");
        using var document = JsonDocument.Parse(body);
        var summary = AssertBoundedShape(document.RootElement.GetProperty("tokenUsageSummary"), SummaryFields);

        Assert.Equal("Complete", summary.GetProperty("completeness").GetString());
        Assert.Equal(2, summary.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(0, summary.GetProperty("attemptsWithUnknownUsage").GetInt32());
        Assert.Equal(3600, summary.GetProperty("inputTokens").GetInt64());
        Assert.Equal(465, summary.GetProperty("outputTokens").GetInt64());
        Assert.Equal(67, summary.GetProperty("cacheCreationInputTokens").GetInt64());
        Assert.Equal(890, summary.GetProperty("cacheReadInputTokens").GetInt64());
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task The_cockpit_reports_no_dispatched_attempts_when_only_undispatched_attempts_exist()
    {
        var runId = await SeedRunAsync();
        await AddAttemptAsync(runId, 1, AgentResponseContract.Proposal, AgentOutcome.SourceChanged, usage: null, dispatched: false);

        var body = await GetOkBodyAsync($"/api/runs/{runId}/cockpit");
        using var document = JsonDocument.Parse(body);
        var summary = AssertBoundedShape(document.RootElement.GetProperty("tokenUsageSummary"), SummaryFields);

        Assert.Equal("NoDispatchedAttempts", summary.GetProperty("completeness").GetString());
        Assert.Equal(0, summary.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(0, summary.GetProperty("attemptsWithUnknownUsage").GetInt32());
        Assert.Equal(0, summary.GetProperty("inputTokens").GetInt64());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("cacheCreationInputTokens").ValueKind);
    }

    [Fact]
    public async Task The_cockpit_reports_no_dispatched_attempts_for_a_run_without_any_attempt()
    {
        var runId = await SeedRunAsync();

        var body = await GetOkBodyAsync($"/api/runs/{runId}/cockpit");
        using var document = JsonDocument.Parse(body);

        Assert.Equal("NoDispatchedAttempts", document.RootElement.GetProperty("tokenUsageSummary").GetProperty("completeness").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("latestAgentAttempt").ValueKind);
    }

    [Fact]
    public async Task The_cockpit_summary_is_scoped_to_its_own_run()
    {
        var measuredRunId = await SeedRunAsync();
        var otherRunId = await SeedRunAsync();
        await AddAttemptAsync(measuredRunId, 1, AgentResponseContract.CriticalReview, AgentOutcome.ProviderInvocationFailed, Usage);
        await AddAttemptAsync(otherRunId, 1, AgentResponseContract.Proposal, AgentOutcome.ProviderInvocationFailed, usage: null);

        var body = await GetOkBodyAsync($"/api/runs/{measuredRunId}/cockpit");
        using var document = JsonDocument.Parse(body);
        var summary = document.RootElement.GetProperty("tokenUsageSummary");

        Assert.Equal("Complete", summary.GetProperty("completeness").GetString());
        Assert.Equal(1, summary.GetProperty("attemptsWithKnownUsage").GetInt32());
    }

    [Theory]
    [InlineData(AgentResponseContract.Proposal, "claude-cli-usage-v1")]
    [InlineData(AgentResponseContract.CriticalReview, "unproven-usage-v1")]
    public async Task Corrupt_provider_schema_pair_is_unknown_in_status_and_cockpit(
        AgentResponseContract contract, string schemaVersion)
    {
        var runId = await SeedRunAsync();
        await AddAttemptAsync(runId, 1, contract, AgentOutcome.ProviderInvocationFailed, usage: null);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE attempts
                   SET AgentInputTokens = {1200}, AgentOutputTokens = {345},
                       AgentCacheCreationInputTokens = {67}, AgentCacheReadInputTokens = {890},
                       AgentTokenUsageSchemaVersion = {schemaVersion}
                   WHERE RunId = {runId}");
        }

        var route = contract == AgentResponseContract.Proposal
            ? "agent-attempts/codex-plan"
            : "agent-attempts/claude-critical-review";
        var statusBody = await GetOkBodyAsync($"/api/runs/{runId}/{route}");
        using var status = JsonDocument.Parse(statusBody);
        var statusUsage = status.RootElement.GetProperty("tokenUsage");
        Assert.All(UsageFields, field => Assert.Equal(JsonValueKind.Null, statusUsage.GetProperty(field).ValueKind));

        var cockpitBody = await GetOkBodyAsync($"/api/runs/{runId}/cockpit");
        using var cockpit = JsonDocument.Parse(cockpitBody);
        var summary = cockpit.RootElement.GetProperty("tokenUsageSummary");
        Assert.Equal("Partial", summary.GetProperty("completeness").GetString());
        Assert.Equal(0, summary.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(1, summary.GetProperty("attemptsWithUnknownUsage").GetInt32());
        Assert.Equal(0, summary.GetProperty("inputTokens").GetInt64());
        Assert.All(UsageFields, field => Assert.Equal(
            JsonValueKind.Null, cockpit.RootElement.GetProperty("latestAgentAttempt").GetProperty("tokenUsage").GetProperty(field).ValueKind));
        AssertNoDisclosure(statusBody);
        AssertNoDisclosure(cockpitBody);
    }

    // A row that could only exist through manual tampering or a future bug, never through this
    // application's own recording path: the correct Codex provider and its own proven schema, but a
    // stray cache value that contract never produces. Unlike Corrupt_provider_schema_pair above
    // (wrong provider/schema pairing), this exercises the shape rule itself end to end, including
    // the run-cockpit aggregate.
    [Fact]
    public async Task A_persisted_codex_row_with_a_stray_cache_value_is_unknown_in_status_and_cockpit()
    {
        var runId = await SeedRunAsync();
        await AddAttemptAsync(runId, 1, AgentResponseContract.Proposal, AgentOutcome.ProviderInvocationFailed, usage: null);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE attempts
                   SET AgentInputTokens = {2400}, AgentOutputTokens = {120},
                       AgentCacheCreationInputTokens = {5}, AgentCacheReadInputTokens = {null},
                       AgentTokenUsageSchemaVersion = {AgentTokenUsageEvidencePolicy.CodexCliSchemaVersion}
                   WHERE RunId = {runId}");
        }

        var statusBody = await GetOkBodyAsync($"/api/runs/{runId}/agent-attempts/codex-plan");
        using var status = JsonDocument.Parse(statusBody);
        var statusUsage = status.RootElement.GetProperty("tokenUsage");
        Assert.All(UsageFields, field => Assert.Equal(JsonValueKind.Null, statusUsage.GetProperty(field).ValueKind));

        var cockpitBody = await GetOkBodyAsync($"/api/runs/{runId}/cockpit");
        using var cockpit = JsonDocument.Parse(cockpitBody);
        var summary = cockpit.RootElement.GetProperty("tokenUsageSummary");
        Assert.Equal("Partial", summary.GetProperty("completeness").GetString());
        Assert.Equal(0, summary.GetProperty("attemptsWithKnownUsage").GetInt32());
        Assert.Equal(1, summary.GetProperty("attemptsWithUnknownUsage").GetInt32());
        Assert.Equal(0, summary.GetProperty("inputTokens").GetInt64());
        AssertNoDisclosure(statusBody);
        AssertNoDisclosure(cockpitBody);
    }

    private static JsonElement AssertBoundedShape(JsonElement element, HashSet<string> fields)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.Equal(fields, element.EnumerateObject().Select(property => property.Name).ToHashSet());
        return element;
    }

    private static void AssertNoDisclosure(string body)
    {
        Assert.DoesNotContain(SchemaVersionSentinel, body, StringComparison.Ordinal);
        Assert.DoesNotContain("schemaVersion", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SessionSentinel, body, StringComparison.Ordinal);
        Assert.DoesNotContain(":\\", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stdout", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stderr", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session", body, StringComparison.OrdinalIgnoreCase);
    }

    private static TimeSpan TimeoutFor(AgentResponseContract contract) => contract switch
    {
        AgentResponseContract.ImplementationReport => TimeSpan.FromMinutes(20),
        _ => TimeSpan.FromMinutes(10),
    };

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<string> GetOkBodyAsync(string path)
    {
        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return body;
    }

    private async Task<Guid> SeedRunAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Usage endpoints", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Expose bounded usage", now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private async Task AddAttemptAsync(
        Guid runId,
        int attemptNumber,
        AgentResponseContract contract,
        AgentOutcome outcome,
        AgentTokenUsageEvidence? usage,
        bool dispatched = true)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var manifestArtifactId = Guid.NewGuid();
        var attempt = contract switch
        {
            AgentResponseContract.Proposal => Attempt.ClaimAgent(
                Guid.NewGuid(), runId, attemptNumber, workspaceId, checkpointId, Fingerprint, manifestArtifactId,
                TimeoutFor(contract), 65536, 131072, now, attemptNumber),
            AgentResponseContract.CriticalReview => Attempt.ClaimAgentCriticalReview(
                Guid.NewGuid(), runId, attemptNumber, workspaceId, checkpointId, Fingerprint, manifestArtifactId,
                TimeoutFor(contract), 65536, 131072, now, attemptNumber),
            AgentResponseContract.ChallengeResolution => Attempt.ClaimAgentChallengeResolution(
                Guid.NewGuid(), runId, attemptNumber, workspaceId, checkpointId, Fingerprint, manifestArtifactId,
                TimeoutFor(contract), 65536, 131072, now, attemptNumber),
            AgentResponseContract.ImplementationReview => Attempt.ClaimAgentCodeReview(
                Guid.NewGuid(), runId, attemptNumber, workspaceId, checkpointId, Fingerprint, manifestArtifactId,
                TimeoutFor(contract), 65536, 131072, now, attemptNumber),
            AgentResponseContract.ImplementationReport => Attempt.ClaimAgentImplementation(
                Guid.NewGuid(), runId, attemptNumber, workspaceId, checkpointId, Fingerprint, manifestArtifactId,
                TimeoutFor(contract), 65536, 131072, now, attemptNumber),
            _ => throw new ArgumentOutOfRangeException(nameof(contract)),
        };

        if (dispatched)
        {
            attempt.MarkAgentDispatched(now);
            attempt.RecordAgentProviderSessionId(SessionSentinel);
        }

        var processEvidence = dispatched
            ? AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(2))
            : null;
        if (contract == AgentResponseContract.ImplementationReport)
        {
            attempt.CompleteImplementation(outcome, null, now.AddSeconds(1), processEvidence, usage);
        }
        else
        {
            attempt.CompleteAgent(outcome, null, now.AddSeconds(1), processEvidence, usage);
        }

        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
    }
}
