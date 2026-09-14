using DevalCopilot.Application.Features.Runs.Commands.CompleteSimulatedRun;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class CompleteSimulatedRunCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static (Project Project, Run Run) CreateRunningRun()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Add token budgets", Now);
        run.Claim(Now);
        return (project, run);
    }

    [Fact]
    public async Task HandleAsync_completes_the_run_and_the_attempt_that_owns_it()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run) = CreateRunningRun();
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CompleteSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new CompleteSimulatedRunCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Single(dbContext.Events, runEvent => runEvent.RunId == run.Id);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_belongs_to_a_different_run()
    {
        await using var dbContext = fixture.CreateContext();
        var (targetProject, targetRun) = CreateRunningRun();
        var (otherProject, otherRun) = CreateRunningRun();
        var attemptForOtherRun = Attempt.Claim(Guid.NewGuid(), otherRun.Id, 1, Now);
        dbContext.Projects.AddRange(targetProject, otherProject);
        dbContext.Runs.AddRange(targetRun, otherRun);
        dbContext.Attempts.Add(attemptForOtherRun);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CompleteSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new CompleteSimulatedRunCommand(targetRun.Id, attemptForOtherRun.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
        // Neither aggregate was mutated and no event was persisted for the cross-run attempt.
        Assert.Equal(RunLifecycle.Running, targetRun.Lifecycle);
        Assert.Equal(AttemptStatus.Running, attemptForOtherRun.Status);
        Assert.Empty(dbContext.Events.Where(runEvent => runEvent.RunId == targetRun.Id || runEvent.RunId == otherRun.Id));
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_is_already_terminal()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run) = CreateRunningRun();
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        attempt.Fail(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CompleteSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new CompleteSimulatedRunCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_active", Assert.Single(result.Errors).Code);
        Assert.Equal(RunLifecycle.Running, run.Lifecycle);
        Assert.Empty(dbContext.Events.Where(runEvent => runEvent.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_run_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, otherRun) = CreateRunningRun();
        var attempt = Attempt.Claim(Guid.NewGuid(), otherRun.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(otherRun);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CompleteSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new CompleteSimulatedRunCommand(Guid.NewGuid(), attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Events.Where(runEvent => runEvent.RunId == otherRun.Id));
    }
}
