using DevalCopilot.Application.Features.Runs.Commands.RecordSimulatedAgentStep;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RecordSimulatedAgentStepCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static (Project Project, Run Run) CreateRunningRun()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}");
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Add token budgets", Now);
        run.Claim(Now);
        return (project, run);
    }

    [Fact]
    public async Task HandleAsync_records_a_step_and_advances_the_run_for_the_attempt_owning_run()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run) = CreateRunningRun();
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordSimulatedAgentStepCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordSimulatedAgentStepCommand(
                run.Id, attempt.Id, RunStage.Plan, ParticipantKind.Codex, RunEventType.CodexProposal, "Proposal"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RunStage.Plan, run.Stage);
        Assert.Single(dbContext.Events, runEvent => runEvent.RunId == run.Id);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run) = CreateRunningRun();
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordSimulatedAgentStepCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordSimulatedAgentStepCommand(
                run.Id, Guid.NewGuid(), RunStage.Plan, ParticipantKind.Codex, RunEventType.CodexProposal, "Proposal"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
        Assert.Equal(RunStage.Intake, run.Stage);
        Assert.Empty(dbContext.Events.Where(runEvent => runEvent.RunId == run.Id));
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

        var handler = new RecordSimulatedAgentStepCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordSimulatedAgentStepCommand(
                targetRun.Id, attemptForOtherRun.Id, RunStage.Plan, ParticipantKind.Codex, RunEventType.CodexProposal, "Proposal"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
        // Neither aggregate was mutated and no event was persisted for the cross-run attempt.
        Assert.Equal(RunStage.Intake, targetRun.Stage);
        Assert.Equal(RunStage.Intake, otherRun.Stage);
        Assert.Empty(dbContext.Events.Where(runEvent => runEvent.RunId == targetRun.Id || runEvent.RunId == otherRun.Id));
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_is_already_terminal()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run) = CreateRunningRun();
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        attempt.Complete(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordSimulatedAgentStepCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordSimulatedAgentStepCommand(
                run.Id, attempt.Id, RunStage.Plan, ParticipantKind.Codex, RunEventType.CodexProposal, "Proposal"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_active", Assert.Single(result.Errors).Code);
        Assert.Equal(RunStage.Intake, run.Stage);
        Assert.Empty(dbContext.Events.Where(runEvent => runEvent.RunId == run.Id));
    }
}
