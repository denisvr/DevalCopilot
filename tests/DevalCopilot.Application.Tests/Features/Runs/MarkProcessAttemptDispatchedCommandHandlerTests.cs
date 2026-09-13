using DevalCopilot.Application.Features.Runs.Commands.MarkProcessAttemptDispatched;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class MarkProcessAttemptDispatchedCommandHandlerTests(SqliteDatabaseFixture fixture)
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
    public async Task HandleAsync_marks_the_attempt_dispatched()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedProcessAttempt();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkProcessAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkProcessAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, result.Value);
        Assert.Equal(Now, attempt.ProcessDispatchedAtUtc);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_dispatched_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedProcessAttempt();
        attempt.MarkProcessDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkProcessAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new MarkProcessAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.already_dispatched", Assert.Single(result.Errors).Code);
        Assert.Equal(Now, attempt.ProcessDispatchedAtUtc);
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

        var handler = new MarkProcessAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkProcessAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_process", Assert.Single(result.Errors).Code);
        Assert.Null(attempt.ProcessDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_terminal_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedProcessAttempt();
        attempt.Fail(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkProcessAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkProcessAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_active", Assert.Single(result.Errors).Code);
        Assert.Null(attempt.ProcessDispatchedAtUtc);
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

        var handler = new MarkProcessAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkProcessAttemptDispatchedCommand(targetRun.Id, otherAttempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
        Assert.Null(otherAttempt.ProcessDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new MarkProcessAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkProcessAttemptDispatchedCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
    }
}
