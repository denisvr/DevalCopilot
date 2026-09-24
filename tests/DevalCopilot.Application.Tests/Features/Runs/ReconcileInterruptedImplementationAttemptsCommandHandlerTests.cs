using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedImplementationAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Owns a fresh database per test method, mirroring
/// <c>ReconcileInterruptedAgentAttemptsCommandHandlerTests</c>'s own reasoning: this handler's
/// query scans every Implementer attempt with no per-run scoping.
/// </summary>
public sealed class ReconcileInterruptedImplementationAttemptsCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 13, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class FakeGitWorkspaceEvidenceReader(GitWorkspaceEvidenceResult result) : IGitWorkspaceEvidenceReader
    {
        public int CallCount { get; private set; }

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private async Task<(Run Run, GitWorkspace Workspace, Attempt Attempt)> SeedRunningImplementerAttemptAsync(
        DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext dbContext, bool dispatched)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Orphaned implementation", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();

        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 1, workspace.Id, Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, Now);
        if (dispatched)
        {
            attempt.MarkAgentDispatched(Now);
        }

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (run, workspace, attempt);
    }

    [Fact]
    public async Task HandleAsync_interrupts_a_never_dispatched_attempt_without_capturing_any_git_evidence()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace, attempt) = await SeedRunningImplementerAttemptAsync(dbContext, dispatched: false);

        var handler = new ReconcileInterruptedImplementationAttemptsCommandHandler(
            dbContext, new FakeGitWorkspaceEvidenceReader(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.GitUnavailable, null, null, [], null)),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Equal(RunLifecycle.Interrupted, run.Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_interrupts_a_dispatched_attempt_and_leaves_the_workspace_ready_when_the_fingerprint_is_unchanged()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace, attempt) = await SeedRunningImplementerAttemptAsync(dbContext, dispatched: true);

        var evidence = new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null);
        var handler = new ReconcileInterruptedImplementationAttemptsCommandHandler(
            dbContext, new FakeGitWorkspaceEvidenceReader(evidence), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Equal(RunLifecycle.Interrupted, run.Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_flags_needs_attention_when_a_dispatched_attempts_fingerprint_no_longer_matches()
    {
        await using var dbContext = _fixture.CreateContext();
        var (run, workspace, attempt) = await SeedRunningImplementerAttemptAsync(dbContext, dispatched: true);

        var evidence = new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('c', 40), new string('c', 64), [], null);
        var handler = new ReconcileInterruptedImplementationAttemptsCommandHandler(
            dbContext, new FakeGitWorkspaceEvidenceReader(evidence), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Equal(RunLifecycle.Interrupted, run.Lifecycle);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_flags_needs_attention_when_post_dispatch_evidence_capture_fails()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, workspace, attempt) = await SeedRunningImplementerAttemptAsync(dbContext, dispatched: true);

        var evidence = new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.GitInvocationFailed, null, null, [], null);
        var handler = new ReconcileInterruptedImplementationAttemptsCommandHandler(
            dbContext, new FakeGitWorkspaceEvidenceReader(evidence), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
    }

    // The role/effect partition this handler relies on: only the WorkspaceMutating role
    // (Implementer) is ever reconciled here — a ReadOnly-contract role's own Running attempt is
    // reconciled instead by ReconcileInterruptedAgentAttemptsCommandHandler, and its evidence is
    // never read by this handler at all. Proves the AgentAttemptContract-derived role set produces
    // exactly today's partition, not a behavior change.
    [Fact]
    public async Task HandleAsync_reconciles_only_the_workspace_mutating_attempt_and_never_reads_evidence_for_a_read_only_attempt()
    {
        await using var dbContext = _fixture.CreateContext();
        var (implementationRun, implementationWorkspace, implementationAttempt) =
            await SeedRunningImplementerAttemptAsync(dbContext, dispatched: true);

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var plannerRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Orphaned planning", Now);
        plannerRun.Claim(Now);
        var plannerAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), plannerRun.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(plannerRun);
        dbContext.Attempts.Add(plannerAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var evidence = new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null);
        var evidenceReader = new FakeGitWorkspaceEvidenceReader(evidence);
        var handler = new ReconcileInterruptedImplementationAttemptsCommandHandler(dbContext, evidenceReader, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        Assert.Equal(1, evidenceReader.CallCount);
        Assert.Equal(AttemptStatus.Interrupted, implementationAttempt.Status);
        Assert.Equal(RunLifecycle.Interrupted, implementationRun.Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, implementationWorkspace.Status);

        dbContext.ChangeTracker.Clear();
        var persistedPlannerAttempt = await dbContext.Attempts.SingleAsync(a => a.Id == plannerAttempt.Id);
        Assert.Equal(AttemptStatus.Running, persistedPlannerAttempt.Status);
    }

    [Fact]
    public async Task HandleAsync_never_invents_process_evidence_and_preserves_assignment_facts_for_an_interrupted_implementation()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, _, attempt) = await SeedRunningImplementerAttemptAsync(dbContext, dispatched: true);

        var evidence = new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null);
        var handler = new ReconcileInterruptedImplementationAttemptsCommandHandler(
            dbContext, new FakeGitWorkspaceEvidenceReader(evidence), new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(new ReconcileInterruptedImplementationAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var verification = _fixture.CreateContext();
        var persisted = await verification.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attempt.Id);
        Assert.Equal(AttemptStatus.Interrupted, persisted.Status);
        Assert.NotNull(persisted.AgentDispatchedAtUtc);
        Assert.Null(persisted.AgentOutcome);
        Assert.Null(persisted.GetAgentProcessExecutionEvidence());
        Assert.Null(persisted.AgentProcessOutcome);
        Assert.Equal(AgentPermissionProfile.WorkspaceEditOnly, persisted.AgentPermissionProfile);
        Assert.Equal("claude-implementation-v1", persisted.AgentAdapterContractVersion);
    }
}
