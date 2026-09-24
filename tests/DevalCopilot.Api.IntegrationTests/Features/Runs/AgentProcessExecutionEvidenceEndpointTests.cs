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
/// Exercises the bounded host-measured process evidence exposed by the Agent status endpoints and
/// the run cockpit through the real MVC pipeline: it is a sibling of the semantic outcome, it
/// carries exactly outcome/exitCode/durationMilliseconds/timeoutMilliseconds, absent evidence is
/// explicit, and no path, argument, environment value, output, manifest, session identifier, or
/// credential reaches a response. Background supervisors are removed by the factory, so every
/// attempt below is seeded terminal before a request is made.
/// </summary>
public sealed class AgentProcessExecutionEvidenceEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private const string SessionSentinel = "provider-session-sentinel-must-not-leak";
    private static readonly string Fingerprint = new('a', 64);
    private static readonly HashSet<string> EvidenceFields = ["outcome", "exitCode", "durationMilliseconds", "timeoutMilliseconds"];

    public static TheoryData<string> ProtectedRoutes => new()
    {
        "agent-attempts/codex-plan",
        "agent-attempts/claude-critical-review",
        "agent-attempts/challenge-resolution",
        "agent-attempts/implementation",
        "agent-attempts/code-review",
        "agent-attempts/review-correction",
        "cockpit",
    };

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Every_evidence_bearing_route_still_requires_authentication(string route)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/{route}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Every_evidence_bearing_route_still_rejects_a_malformed_run_identifier(string route)
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/runs/not-a-run-id/{route}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public static TheoryData<string, AgentResponseContract> RoleRoutes => new()
    {
        { "agent-attempts/codex-plan", AgentResponseContract.Proposal },
        { "agent-attempts/claude-critical-review", AgentResponseContract.CriticalReview },
        { "agent-attempts/challenge-resolution", AgentResponseContract.ChallengeResolution },
        { "agent-attempts/code-review", AgentResponseContract.ImplementationReview },
        { "agent-attempts/implementation", AgentResponseContract.ImplementationReport },
    };

    [Theory]
    [MemberData(nameof(RoleRoutes))]
    public async Task Each_role_status_exposes_timed_out_evidence_as_a_sibling_of_the_semantic_outcome(
        string route, AgentResponseContract contract)
    {
        var timedOut = AgentProcessExecutionEvidence.Create(ProcessOutcome.TimedOut, null, TimeSpan.FromMilliseconds(600_123));
        var (runId, manifestArtifactId) = await SeedTerminalAttemptAsync(contract, AgentOutcome.ProviderInvocationFailed, timedOut);

        var body = await GetOkBodyAsync($"/api/runs/{runId}/{route}");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal("ProviderInvocationFailed", root.GetProperty("outcome").GetString());
        var evidence = AssertBoundedEvidenceShape(root.GetProperty("processExecution"));
        Assert.Equal("TimedOut", evidence.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("exitCode").ValueKind);
        Assert.Equal(600_123, evidence.GetProperty("durationMilliseconds").GetInt64());
        Assert.Equal(TimeoutFor(contract).TotalMilliseconds, evidence.GetProperty("timeoutMilliseconds").GetInt64());
        AssertNoDisclosure(body, manifestArtifactId);
    }

    [Fact]
    public async Task A_clean_exit_with_a_semantic_success_exposes_exit_code_zero_and_the_success_separately()
    {
        var (runId, manifestArtifactId) = await SeedTerminalAttemptAsync(
            AgentResponseContract.Proposal, AgentOutcome.Proposed, TestProcessEvidence.CleanExit);

        var body = await GetOkBodyAsync($"/api/runs/{runId}/agent-attempts/codex-plan");
        using var document = JsonDocument.Parse(body);
        var evidence = AssertBoundedEvidenceShape(document.RootElement.GetProperty("processExecution"));

        Assert.Equal("Proposed", document.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("Exited", evidence.GetProperty("outcome").GetString());
        Assert.Equal(0, evidence.GetProperty("exitCode").GetInt32());
        Assert.Equal(1250, evidence.GetProperty("durationMilliseconds").GetInt64());
        AssertNoDisclosure(body, manifestArtifactId);
    }

    [Fact]
    public async Task Absent_evidence_is_explicit_and_distinct_from_the_semantic_outcome()
    {
        var (runId, manifestArtifactId) = await SeedTerminalAttemptAsync(
            AgentResponseContract.CriticalReview, AgentOutcome.ProviderInvocationFailed, evidence: null);

        var body = await GetOkBodyAsync($"/api/runs/{runId}/agent-attempts/claude-critical-review");
        using var document = JsonDocument.Parse(body);
        var evidence = AssertBoundedEvidenceShape(document.RootElement.GetProperty("processExecution"));

        Assert.Equal("ProviderInvocationFailed", document.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("outcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("exitCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("durationMilliseconds").ValueKind);
        Assert.Equal(600_000, evidence.GetProperty("timeoutMilliseconds").GetInt64());
        AssertNoDisclosure(body, manifestArtifactId);
    }

    [Fact]
    public async Task A_run_without_an_attempt_has_no_process_execution_object_at_all()
    {
        var runId = await SeedRunAsync();

        var body = await GetOkBodyAsync($"/api/runs/{runId}/agent-attempts/code-review");
        using var document = JsonDocument.Parse(body);

        Assert.False(document.RootElement.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("processExecution").ValueKind);
    }

    [Fact]
    public async Task The_cockpit_exposes_only_the_latest_agent_attempts_bounded_evidence()
    {
        var runId = await SeedRunAsync();
        var cancelled = AgentProcessExecutionEvidence.Create(ProcessOutcome.Cancelled, null, TimeSpan.FromSeconds(4));
        await AddTerminalAttemptAsync(runId, 1, AgentResponseContract.Proposal, AgentOutcome.Proposed, TestProcessEvidence.CleanExit);
        var manifestArtifactId = await AddTerminalAttemptAsync(
            runId, 2, AgentResponseContract.CriticalReview, AgentOutcome.ProviderInvocationFailed, cancelled);

        var body = await GetOkBodyAsync($"/api/runs/{runId}/cockpit");
        using var document = JsonDocument.Parse(body);
        var latest = document.RootElement.GetProperty("latestAgentAttempt");

        Assert.Equal(2, latest.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("CriticalReviewer", latest.GetProperty("role").GetString());
        Assert.Equal("ClaudeCode", latest.GetProperty("provider").GetString());
        Assert.Equal("Failed", latest.GetProperty("status").GetString());
        Assert.Equal("ProviderInvocationFailed", latest.GetProperty("outcome").GetString());
        var evidence = AssertBoundedEvidenceShape(latest.GetProperty("processExecution"));
        Assert.Equal("Cancelled", evidence.GetProperty("outcome").GetString());
        Assert.Equal(4000, evidence.GetProperty("durationMilliseconds").GetInt64());
        AssertNoDisclosure(body, manifestArtifactId);
    }

    [Fact]
    public async Task The_cockpit_reports_no_latest_agent_attempt_for_a_run_without_one()
    {
        var runId = await SeedRunAsync();

        var body = await GetOkBodyAsync($"/api/runs/{runId}/cockpit");
        using var document = JsonDocument.Parse(body);

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("latestAgentAttempt").ValueKind);
    }

    private static JsonElement AssertBoundedEvidenceShape(JsonElement evidence)
    {
        Assert.Equal(JsonValueKind.Object, evidence.ValueKind);
        Assert.Equal(EvidenceFields, evidence.EnumerateObject().Select(property => property.Name).ToHashSet());
        return evidence;
    }

    private static void AssertNoDisclosure(string body, Guid manifestArtifactId)
    {
        Assert.DoesNotContain(SessionSentinel, body, StringComparison.Ordinal);
        Assert.DoesNotContain(manifestArtifactId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":\\", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stdout", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stderr", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("argument", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manifest", body, StringComparison.OrdinalIgnoreCase);
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
        var project = Project.Register(Guid.NewGuid(), "Evidence endpoints", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Expose bounded evidence", now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync();
        return run.Id;
    }

    private async Task<(Guid RunId, Guid ManifestArtifactId)> SeedTerminalAttemptAsync(
        AgentResponseContract contract, AgentOutcome outcome, AgentProcessExecutionEvidence? evidence)
    {
        var runId = await SeedRunAsync();
        var manifestArtifactId = await AddTerminalAttemptAsync(runId, 1, contract, outcome, evidence);
        return (runId, manifestArtifactId);
    }

    private async Task<Guid> AddTerminalAttemptAsync(
        Guid runId, int attemptNumber, AgentResponseContract contract, AgentOutcome outcome, AgentProcessExecutionEvidence? evidence)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var manifestArtifactId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
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

        attempt.MarkAgentDispatched(now);
        attempt.RecordAgentProviderSessionId(SessionSentinel);
        if (contract == AgentResponseContract.ImplementationReport)
        {
            attempt.CompleteImplementation(outcome, null, now.AddSeconds(1), evidence);
        }
        else
        {
            attempt.CompleteAgent(outcome, outcome == AgentOutcome.Proposed ? Fingerprint : null, now.AddSeconds(1), evidence);
        }

        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0));
        await dbContext.SaveChangesAsync();
        return manifestArtifactId;
    }
}
