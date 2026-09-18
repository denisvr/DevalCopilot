using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class MarkAgentAttemptDispatchedCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static (Project Project, Run Run, Attempt Attempt) CreateClaimedAgentAttempt()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        return (project, run, attempt);
    }

    /// <summary>
    /// A fully eligible Agent attempt — Running run, Ready workspace, Active lease, current
    /// checkpoint — the exact shape <see cref="MarkAgentAttemptDispatchedCommandHandler"/>'s own
    /// last-gate revalidation requires. Each knob independently breaks exactly one of those facts
    /// so a test can prove that specific gate, and nothing else, causes the rejection.
    /// </summary>
    private static async Task<(Run Run, Attempt Attempt)> SeedEligibleAttemptAsync(
        DevalCopilotDbContext dbContext,
        bool runRunning = true,
        bool workspaceReady = true,
        bool leaseActive = true,
        bool checkpointIsCurrent = true)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        if (runRunning)
        {
            run.Claim(Now);
        }

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        if (workspaceReady)
        {
            workspace.MarkReady();
        }

        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);

        // A distinct physical identity per call: this class's fixture shares one database
        // across every test method, and the Active-lease unique constraint is keyed by
        // (PhysicalVolumeSerialNumber, PhysicalFileId) — reusing a fixed identity here would
        // collide with another test's still-Active lease.
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now);
        if (!leaseActive)
        {
            lease.Release(Now);
        }

        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.Attempts.Add(attempt);

        if (!checkpointIsCurrent)
        {
            // A newer checkpoint now exists for the workspace, so the attempt's own claimed
            // checkpoint is no longer the workspace's current one.
            dbContext.GitCheckpoints.Add(
                GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), new string('b', 64), []));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (run, attempt);
    }

    [Fact]
    public async Task HandleAsync_marks_the_attempt_dispatched()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, result.Value);
        Assert.Equal(Now, attempt.AgentDispatchedAtUtc);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    // The authoritative last gate: GetEligibleAgentAttemptsQuery is only a snapshot, and
    // workspace/lease/checkpoint state can change during the pre-dispatch Git evidence capture
    // that runs between that snapshot and this call. Each of these proves one such change, alone,
    // is enough to reject dispatch with the same safe, distinguishable conflict code the
    // supervisor uses to resolve the attempt as WorkspaceNoLongerEligible — never setting the
    // dispatch marker.
    [Fact]
    public async Task HandleAsync_rejects_dispatch_when_the_run_is_no_longer_running()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext, runRunning: false);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_rejects_dispatch_when_the_workspace_is_no_longer_ready()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext, workspaceReady: false);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_rejects_dispatch_when_the_lease_is_no_longer_active()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext, leaseActive: false);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_rejects_dispatch_when_the_claimed_checkpoint_is_no_longer_current()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext, checkpointIsCurrent: false);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_dispatched_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedAgentAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.already_dispatched", Assert.Single(result.Errors).Code);
        Assert.Equal(Now, attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_a_simulated_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Simulated run", Now);
        run.Claim(Now);
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_agent", Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_a_process_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Process run", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(
            Guid.NewGuid(), run.Id, 1,
            new ProcessExecutionIntent(@"C:\tools\build.exe", ["--verify"], @"C:\repos\devalcopilot", @"C:\repos", TimeSpan.FromMinutes(5), 65536, 131072),
            Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_agent", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_terminal_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedAgentAttempt();
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_active", Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_belongs_to_a_different_run()
    {
        await using var dbContext = fixture.CreateContext();
        var (targetProject, targetRun, _) = CreateClaimedAgentAttempt();
        var (otherProject, otherRun, otherAttempt) = CreateClaimedAgentAttempt();
        dbContext.Projects.AddRange(targetProject, otherProject);
        dbContext.Runs.AddRange(targetRun, otherRun);
        dbContext.Attempts.Add(otherAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(targetRun.Id, otherAttempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
        Assert.Null(otherAttempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
    }
}
