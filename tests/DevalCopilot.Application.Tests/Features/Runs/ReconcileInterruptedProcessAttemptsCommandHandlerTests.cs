using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedProcessAttempts;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Owns a fresh database per test method rather than a shared <see cref="IClassFixture{T}"/>:
/// the reconciliation query scans every attempt with no per-run scoping, so a shared database
/// would let one test's rows leak into another test's "nothing eligible" assertion.
/// </summary>
public sealed class ReconcileInterruptedProcessAttemptsCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static ProcessExecutionIntent CreateIntent() => new(
        ExecutablePath: @"C:\tools\build.exe",
        Arguments: ["--verify"],
        WorkingDirectory: @"C:\repos\devalcopilot",
        ApprovedRoot: @"C:\repos",
        Timeout: TimeSpan.FromMinutes(5),
        MaxBytesPerStream: 65536,
        MaxTotalCapturedBytes: 131072);

    [Fact]
    public async Task HandleAsync_interrupts_a_running_process_attempt_and_its_running_run_together()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Orphaned by a crash", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReconcileInterruptedProcessAttemptsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedProcessAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        Assert.Equal(AttemptStatus.Interrupted, attempt.Status);
        Assert.Equal(RunLifecycle.Interrupted, run.Lifecycle);
    }

    [Fact]
    public async Task HandleAsync_never_touches_a_running_simulated_attempt()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Simulated run", Now);
        run.Claim(Now);
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReconcileInterruptedProcessAttemptsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedProcessAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(RunLifecycle.Running, run.Lifecycle);
    }

    [Fact]
    public async Task HandleAsync_never_touches_an_already_terminal_process_attempt()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Already completed", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), Now);
        attempt.CompleteProcess(ProcessOutcome.Exited, 0, Now);
        run.Complete(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReconcileInterruptedProcessAttemptsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedProcessAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
    }

    [Fact]
    public async Task HandleAsync_fails_without_mutating_anything_when_a_running_process_attempts_run_is_not_running()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Inconsistent state", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // The run moved to a terminal state without its attempt following — an inconsistency
        // reconciliation must detect and refuse, not paper over by interrupting the attempt
        // alone.
        run.Complete(Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReconcileInterruptedProcessAttemptsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedProcessAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.inconsistent_run_state", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
    }

    [Fact]
    public async Task HandleAsync_fails_without_mutating_anything_when_a_running_process_attempt_has_no_owning_run()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Orphaned attempt", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Removes only the run row, bypassing the cascade FK (which would otherwise also
        // remove the attempt) to construct a genuinely orphaned attempt — the inconsistent
        // state the handler must detect rather than silently interrupt.
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM runs WHERE Id = {0}", run.Id);
        dbContext.ChangeTracker.Clear();

        var handler = new ReconcileInterruptedProcessAttemptsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedProcessAttemptsCommand(), CancellationToken.None);

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

        var handler = new ReconcileInterruptedProcessAttemptsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new ReconcileInterruptedProcessAttemptsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }
}
