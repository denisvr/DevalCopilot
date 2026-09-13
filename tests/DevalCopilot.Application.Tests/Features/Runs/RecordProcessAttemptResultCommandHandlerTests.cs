using DevalCopilot.Application.Features.Runs.Commands.RecordProcessAttemptResult;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RecordProcessAttemptResultCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static ProcessExecutionIntent CreateIntent() => new(
        ExecutablePath: @"C:\tools\build.exe",
        Arguments: ["--verify"],
        WorkingDirectory: @"C:\repos\devalcopilot",
        ApprovedRoot: @"C:\repos",
        Timeout: TimeSpan.FromMinutes(5),
        MaxBytesPerStream: 65536,
        MaxTotalCapturedBytes: 131072);

    private static (Project Project, Run Run, Attempt Attempt) CreateClaimedProcessAttempt()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Run a real command", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, 1, CreateIntent(), Now);
        return (project, run, attempt);
    }

    [Fact]
    public async Task HandleAsync_completes_the_attempt_and_the_run_for_a_zero_exit_code()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedProcessAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordProcessAttemptResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordProcessAttemptResultCommand(run.Id, attempt.Id, ProcessOutcome.Exited, 0), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Completed, result.Value);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(127)]
    public async Task HandleAsync_fails_the_attempt_and_the_run_for_a_non_zero_exit_code(int exitCode)
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedProcessAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordProcessAttemptResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordProcessAttemptResultCommand(run.Id, attempt.Id, ProcessOutcome.Exited, exitCode), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(RunLifecycle.Failed, run.Lifecycle);
    }

    [Theory]
    [InlineData(ProcessOutcome.TimedOut)]
    [InlineData(ProcessOutcome.Cancelled)]
    public async Task HandleAsync_fails_the_attempt_and_the_run_for_a_timeout_or_cancellation(ProcessOutcome outcome)
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedProcessAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordProcessAttemptResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordProcessAttemptResultCommand(run.Id, attempt.Id, outcome, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(outcome, attempt.ProcessOutcome);
        Assert.Equal(RunLifecycle.Failed, run.Lifecycle);
    }

    [Fact]
    public async Task HandleAsync_fails_the_attempt_and_the_run_with_no_recorded_outcome_when_no_result_was_produced()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedProcessAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordProcessAttemptResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordProcessAttemptResultCommand(run.Id, attempt.Id, Outcome: null, ExitCode: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Null(attempt.ProcessOutcome);
        Assert.Null(attempt.ProcessExitCode);
        Assert.Equal(RunLifecycle.Failed, run.Lifecycle);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_belongs_to_a_different_run()
    {
        await using var dbContext = fixture.CreateContext();
        var (targetProject, targetRun, _) = CreateClaimedProcessAttempt();
        var (otherProject, otherRun, otherAttempt) = CreateClaimedProcessAttempt();
        dbContext.Projects.AddRange(targetProject, otherProject);
        dbContext.Runs.AddRange(targetRun, otherRun);
        dbContext.Attempts.Add(otherAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordProcessAttemptResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordProcessAttemptResultCommand(targetRun.Id, otherAttempt.Id, ProcessOutcome.Exited, 0), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
        Assert.Equal(RunLifecycle.Running, targetRun.Lifecycle);
        Assert.Equal(AttemptStatus.Running, otherAttempt.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_a_simulated_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Simulated run", Now);
        run.Claim(Now);
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordProcessAttemptResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordProcessAttemptResultCommand(run.Id, attempt.Id, ProcessOutcome.Exited, 0), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_process", Assert.Single(result.Errors).Code);
        Assert.Equal(RunLifecycle.Running, run.Lifecycle);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_terminal_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedProcessAttempt();
        attempt.CompleteProcess(ProcessOutcome.Exited, 0, Now);
        run.Complete(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordProcessAttemptResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordProcessAttemptResultCommand(run.Id, attempt.Id, ProcessOutcome.Exited, 1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_active", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Equal(0, attempt.ProcessExitCode);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_run_or_attempt_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new RecordProcessAttemptResultCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordProcessAttemptResultCommand(Guid.NewGuid(), Guid.NewGuid(), ProcessOutcome.Exited, 0), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }
}
