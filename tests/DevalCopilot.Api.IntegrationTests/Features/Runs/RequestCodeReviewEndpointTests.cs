using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.Features.Runs.RequestCodeReview;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises <c>POST /api/runs/{runId}/agent-attempts/code-review</c> against the real Api host:
/// authentication, the golden path, and a representative subset of the distinct conflict/not-found
/// codes <c>CreateCodeReviewAttemptCommandHandler</c> can produce. Mirrors
/// <c>RequestChallengeResolutionEndpointTests</c>'s hosting/seeding style, scoped down to what is
/// genuinely specific to this new stage — the exhaustive workspace/lease/checkpoint/verification
/// eligibility permutations are already proven at the handler-unit level in
/// <c>CreateCodeReviewAttemptCommandHandlerTests</c> and do not need duplicating end to end through
/// a real HTTP host for every branch.
/// </summary>
public sealed class RequestCodeReviewEndpointTests : IDisposable
{
    private static readonly string MatchingFingerprint = CodeReviewApiWebApplicationFactory.MatchingFingerprint;

    private static readonly string ValidProposalStructuredContentJson = JsonSerializer.Serialize(new
    {
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private readonly CodeReviewApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    [Fact]
    public async Task Requires_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/runs/{Guid.NewGuid()}/agent-attempts/code-review", new RequestCodeReviewRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_run()
    {
        using var client = CreateAuthenticatedClient();

        var response = await PostCodeReviewAsync(client, Guid.NewGuid(), Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("runs.not_found", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_run_is_not_running()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(claimRun: false);
        using var client = CreateAuthenticatedClient();

        var response = await PostCodeReviewAsync(client, runId, Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("runs.not_running", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_a_safe_not_found_response_for_an_unknown_execution_report()
    {
        var (runId, _, _) = await SeedEligibleRunAsync(claimRun: true, codexObserved: true);
        using var client = CreateAuthenticatedClient();

        var response = await PostCodeReviewAsync(client, runId, Guid.NewGuid());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("agent_attempts.execution_report_not_found", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_no_verification_command_is_enabled()
    {
        var (runId, workspaceId, resultCheckpointId) = await SeedEligibleRunAsync(claimRun: true, codexObserved: true);
        var executionReportId = await SeedImplementedExecutionAsync(_factory, runId, workspaceId, resultCheckpointId);

        using var client = CreateAuthenticatedClient();
        var response = await PostCodeReviewAsync(client, runId, executionReportId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.no_verification_commands_enabled", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
    }

    [Fact]
    public async Task Returns_conflict_when_the_workspace_source_has_drifted_since_the_checkpoint_was_captured()
    {
        await using var driftFactory = new CodeReviewCheckpointDriftApiWebApplicationFactory();
        var (runId, workspaceId, resultCheckpointId) = await SeedEligibleRunAsync(driftFactory, claimRun: true, codexObserved: true);
        var executionReportId = await SeedImplementedExecutionAsync(driftFactory, runId, workspaceId, resultCheckpointId);

        using var client = driftFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);

        var response = await PostCodeReviewAsync(client, runId, executionReportId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("agent_attempts.checkpoint_not_current", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
        Assert.DoesNotContain(CodeReviewCheckpointDriftApiWebApplicationFactory.DriftedFingerprint, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Succeeds_and_returns_the_attempt_identity_only()
    {
        var (runId, workspaceId, resultCheckpointId) = await SeedEligibleRunAsync(claimRun: true, codexObserved: true);
        var executionReportId = await SeedImplementedExecutionAsync(_factory, runId, workspaceId, resultCheckpointId);
        await SeedPassingVerificationAsync(_factory, runId, workspaceId, resultCheckpointId);

        using var client = CreateAuthenticatedClient();
        var response = await PostCodeReviewAsync(client, runId, executionReportId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RequestCodeReviewResponse>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.AttemptId);

        // The success body carries only the attempt identity — never the checkpoint fingerprint,
        // the workspace path, the resolved Codex executable path, the verification command's
        // executable, or any implementation/verification content this run was seeded with.
        AssertNoDisclosure(body);
    }

    private static Task<HttpResponseMessage> PostCodeReviewAsync(HttpClient client, Guid runId, Guid executionReportMessageId) =>
        client.PostAsJsonAsync(
            $"/api/runs/{runId}/agent-attempts/code-review", new RequestCodeReviewRequest(executionReportMessageId));

    private static void AssertNoDisclosure(string body)
    {
        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(MatchingFingerprint, body, StringComparison.Ordinal);
        Assert.DoesNotContain("resolvedExecutablePath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawOutput", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
    }

    /// <summary>Seeds a complete, valid successful implementation chain directly against Domain: a
    /// completed Codex planning attempt and its Proposal, and a completed Implementer attempt whose
    /// real result checkpoint is <paramref name="resultCheckpointId"/> with its one
    /// provider-observed ExecutionReport replying to the resolved plan — the exact evidence
    /// <c>CreateCodeReviewAttemptCommandHandler</c> requires (verification evidence is seeded
    /// separately, only where a test needs it).</summary>
    internal static async Task<Guid> SeedImplementedExecutionAsync(
        WebApplicationFactory<Program> factory, Guid runId, Guid workspaceId, Guid resultCheckpointId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var startingCheckpoint = await dbContext.GitCheckpoints
            .Where(checkpoint => checkpoint.WorkspaceId == workspaceId && checkpoint.Id != resultCheckpointId)
            .OrderBy(checkpoint => checkpoint.CheckpointNumber)
            .FirstAsync();

        var planningAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, 1, workspaceId, startingCheckpoint.Id, startingCheckpoint.FingerprintSha256, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now);
        planningAttempt.MarkAgentDispatched(now.AddSeconds(1));
        planningAttempt.CompleteAgent(AgentOutcome.Proposed, startingCheckpoint.FingerprintSha256, now.AddSeconds(2));
        dbContext.Attempts.Add(planningAttempt);

        var resolvedPlan = CollaborationMessage.Record(
            Guid.NewGuid(), runId, planningAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Codex, ParticipantKind.Claude, CollaborationMessageType.Proposal, null,
            "Add the ledger table and its query.", ValidProposalStructuredContentJson,
            CollaborationMessageProvenance.ProviderObserved, now.AddSeconds(2));
        dbContext.CollaborationMessages.Add(resolvedPlan);

        var implementerAttempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), runId, 2, workspaceId, startingCheckpoint.Id, startingCheckpoint.FingerprintSha256, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now.AddSeconds(3));
        implementerAttempt.MarkAgentDispatched(now.AddSeconds(4));
        implementerAttempt.CompleteImplementation(AgentOutcome.Implemented, resultCheckpointId, now.AddSeconds(5));
        dbContext.Attempts.Add(implementerAttempt);

        var executionReport = CollaborationMessage.Record(
            Guid.NewGuid(), runId, implementerAttempt.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantKind.Claude, ParticipantKind.Codex, CollaborationMessageType.ExecutionReport, resolvedPlan.Id,
            "Added the ledger table and its query.",
            JsonSerializer.Serialize(new { completedWork = "Added table and query.", verification = "dotnet test" }),
            CollaborationMessageProvenance.ProviderObserved, now.AddSeconds(5));
        dbContext.CollaborationMessages.Add(executionReport);

        await dbContext.SaveChangesAsync();

        return executionReport.Id;
    }

    internal static async Task SeedPassingVerificationAsync(
        WebApplicationFactory<Program> factory, Guid runId, Guid workspaceId, Guid resultCheckpointId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var workspace = await dbContext.GitWorkspaces.SingleAsync(w => w.Id == workspaceId);
        var checkpoint = await dbContext.GitCheckpoints.SingleAsync(c => c.Id == resultCheckpointId);
        var project = await dbContext.Projects.SingleAsync(p => p.Id == workspace.ProjectId);

        var command = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, 1, "Backend tests", @"C:\dotnet.exe", ["test"], 300, true, now);
        dbContext.VerificationCommands.Add(command);
        await dbContext.SaveChangesAsync();

        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, command, now);
        execution.MarkDispatched(now);
        execution.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, now);
        dbContext.VerificationExecutions.Add(execution);
        await dbContext.SaveChangesAsync();
    }

    private Task<(Guid RunId, Guid WorkspaceId, Guid ResultCheckpointId)> SeedEligibleRunAsync(
        bool claimRun = false, bool codexObserved = false) =>
        SeedEligibleRunAsync(_factory, claimRun, codexObserved);

    private static async Task<(Guid RunId, Guid WorkspaceId, Guid ResultCheckpointId)> SeedEligibleRunAsync(
        WebApplicationFactory<Program> factory, bool claimRun = false, bool codexObserved = false)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;

        var project = Project.Register(Guid.NewGuid(), "Code review project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the implementation", now);
        if (claimRun)
        {
            run.Claim(now);
        }

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        dbContext.GitWorkspaces.Add(workspace);

        // The starting checkpoint the Implementer began from, plus the real result checkpoint its
        // successful implementation produced — the current checkpoint the review must claim.
        var startingCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), MatchingFingerprint, []);
        dbContext.GitCheckpoints.Add(startingCheckpoint);
        var resultCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 2, now, new string('b', 40), MatchingFingerprint.Replace('a', 'b'), []);
        dbContext.GitCheckpoints.Add(resultCheckpoint);

        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now);
        dbContext.RepositoryMutationLeases.Add(lease);

        await dbContext.SaveChangesAsync();

        if (codexObserved)
        {
            var codex = await dbContext.HostCapabilitySnapshots.SingleAsync(s => s.Capability == Capability.CodexCli);
            codex.MarkDispatched(now);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
            await dbContext.SaveChangesAsync();
        }

        return (run.Id, workspace.Id, resultCheckpoint.Id);
    }
}

/// <summary>
/// The real Api host with two narrow test seams: a fixed <see cref="IGitWorkspaceEvidenceReader"/>
/// and the same set of background hosted services removed as
/// <c>ChallengeResolutionApiWebApplicationFactory</c>, plus
/// <see cref="ImplementationReviewSupervisor"/> and <see cref="ImplementationSupervisor"/> —
/// otherwise they would race a test's own deterministic seeding of <c>Attempt</c> rows. Mirrors
/// <c>ChallengeResolutionApiWebApplicationFactory</c> exactly.
/// </summary>
public sealed class CodeReviewApiWebApplicationFactory : ApiWebApplicationFactory
{
    public const string MatchingFingerprint =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(new CodexPlanningTestHostSeams.FixedEvidenceReader(MatchingFingerprint));

            CodexPlanningTestHostSeams.RemoveHostedService<HostCapabilityReadinessSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<AgentAttemptSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<ClaudeCriticalReviewSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<ChallengeResolutionSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<ImplementationSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<ImplementationReviewSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<SimulatedRunSupervisor>(services);
        });
    }
}

/// <summary>
/// Same real host as <see cref="CodeReviewApiWebApplicationFactory"/>, except its fixed evidence
/// reader reports a fingerprint that deliberately never matches any checkpoint a test seeds —
/// proving <c>CreateCodeReviewAttemptCommandHandler</c>'s fresh, pre-dispatch evidence recheck
/// produces "agent_attempts.checkpoint_not_current". Mirrors
/// <c>ChallengeResolutionCheckpointDriftApiWebApplicationFactory</c>.
/// </summary>
public sealed class CodeReviewCheckpointDriftApiWebApplicationFactory : ApiWebApplicationFactory
{
    public const string DriftedFingerprint =
        "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(new CodexPlanningTestHostSeams.FixedEvidenceReader(DriftedFingerprint));

            CodexPlanningTestHostSeams.RemoveHostedService<HostCapabilityReadinessSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<AgentAttemptSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<ClaudeCriticalReviewSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<ChallengeResolutionSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<ImplementationSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<ImplementationReviewSupervisor>(services);
            CodexPlanningTestHostSeams.RemoveHostedService<SimulatedRunSupervisor>(services);
        });
    }
}
