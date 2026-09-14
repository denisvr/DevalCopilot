using DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class StartSimulatedRunCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_records_intent_with_created_lifecycle_and_a_run_started_event()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\intent-test", Now);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new StartSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new StartSimulatedRunCommand(project.Id, "Add token budgets"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ExecutionNumber);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var persistedRun = await dbContext.Runs.FindAsync([result.Value.RunId], CancellationToken.None);
        Assert.NotNull(persistedRun);
        Assert.Equal(RunLifecycle.Created, persistedRun.Lifecycle);
        Assert.Equal(RunStage.Intake, persistedRun.Stage);

        var runStartedEvent = Assert.Single(dbContext.Events, runEvent => runEvent.RunId == result.Value.RunId);
        Assert.Equal(RunEventType.RunStarted, runStartedEvent.EventType);
        Assert.Equal(ParticipantKind.Orchestrator, runStartedEvent.Actor);
    }

    [Fact]
    public async Task HandleAsync_reserves_increasing_execution_numbers_for_the_same_project()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\execution-number-test", Now);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new StartSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now));

        var first = await handler.HandleAsync(
            new StartSimulatedRunCommand(project.Id, "First run"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var second = await handler.HandleAsync(
            new StartSimulatedRunCommand(project.Id, "Second run"), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1, first.Value.ExecutionNumber);
        Assert.Equal(2, second.Value.ExecutionNumber);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_project_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();
        var handler = new StartSimulatedRunCommandHandler(dbContext, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new StartSimulatedRunCommand(Guid.NewGuid(), "Add token budgets"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("projects.not_found", Assert.Single(result.Errors).Code);
    }
}
