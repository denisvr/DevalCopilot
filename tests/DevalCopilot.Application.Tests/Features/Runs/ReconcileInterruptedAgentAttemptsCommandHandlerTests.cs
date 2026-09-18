using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Owns a fresh database per test method rather than a shared <see cref="IClassFixture{T}"/>:
/// the reconciliation query scans every attempt with no per-run scoping, so a shared database
/// would let one test's rows leak into another test's "nothing eligible" assertion. Mirrors
/// <c>ReconcileInterruptedProcessAttemptsCommandHandlerTests</c>.
/// </summary>
public sealed class ReconcileInterruptedAgentAttemptsCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 13, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static Attempt ClaimAgentAttempt(Guid runId, int attemptNumber = 1) => Attempt.ClaimAgent(
        Guid.NewGuid(), runId, attemptNumber, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
        TimeSpan.FromMinutes(10), 262144, 524288, Now);

    [Fact]
    public async Task HandleAsync_interrupts_a_running_agent_attempt_and_its_running_run_together()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Orphaned by a crash", Now);
        run.Claim(Now);
        var attempt = ClaimAgentAttempt(run.Id);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReconcileInterruptedAgentAttemptsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Equal(RunLifecycle.Interrupted, run.Lifecycle);
    }

    [Fact]
    public async Task HandleAsync_never_touches_a_running_process_or_simulated_attempt()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);

        var processRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "process", Now);
        processRun.Claim(Now);
        var processAttempt = Attempt.ClaimProcess(
            Guid.NewGuid(), processRun.Id, 1,
            new ProcessExecutionIntent(@"C:\tools\build.exe", ["--verify"], @"C:\repos\devalcopilot", @"C:\repos", TimeSpan.FromMinutes(5), 65536, 131072),
            Now);

        var simulatedRun = Run.RecordIntent(Guid.NewGuid(), project.Id, 2, "simulated", Now);
        simulatedRun.Claim(Now);
        var simulatedAttempt = Attempt.Claim(Guid.NewGuid(), simulatedRun.Id, 1, Now);

        dbContext.Runs.AddRange(processRun, simulatedRun);
        dbContext.Attempts.AddRange(processAttempt, simulatedAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReconcileInterruptedAgentAttemptsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Equal(AttemptStatus.Running, processAttempt.Status);
        Assert.Equal(AttemptStatus.Running, simulatedAttempt.Status);
        Assert.Equal(RunLifecycle.Running, processRun.Lifecycle);
        Assert.Equal(RunLifecycle.Running, simulatedRun.Lifecycle);
    }

    [Fact]
    public async Task HandleAsync_never_touches_an_already_terminal_agent_attempt()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Already completed", Now);
        run.Claim(Now);
        var attempt = ClaimAgentAttempt(run.Id);
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, Now);
        run.Complete(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReconcileInterruptedAgentAttemptsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
    }

    [Fact]
    public async Task HandleAsync_fails_without_mutating_anything_when_a_running_agent_attempts_run_is_not_running()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Inconsistent state", Now);
        run.Claim(Now);
        var attempt = ClaimAgentAttempt(run.Id);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // The run moved to a terminal state without its attempt following — an inconsistency
        // reconciliation must detect and refuse, not paper over by interrupting the attempt alone.
        run.Complete(Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReconcileInterruptedAgentAttemptsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.inconsistent_run_state", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
    }

    [Fact]
    public async Task HandleAsync_fails_without_mutating_anything_when_a_running_agent_attempt_has_no_owning_run()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Orphaned attempt", Now);
        run.Claim(Now);
        var attempt = ClaimAgentAttempt(run.Id);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM runs WHERE Id = {0}", run.Id);
        dbContext.ChangeTracker.Clear();

        var handler = new ReconcileInterruptedAgentAttemptsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.orphaned_run", Assert.Single(result.Errors).Code);

        dbContext.ChangeTracker.Clear();
        var persistedAttempt = await dbContext.Attempts.FindAsync(attempt.Id);
        Assert.NotNull(persistedAttempt);
        Assert.Equal(AttemptStatus.Running, persistedAttempt.Status);
    }

    [Fact]
    public async Task HandleAsync_returns_zero_when_nothing_is_eligible()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new ReconcileInterruptedAgentAttemptsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedAgentAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }
}
