using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

public sealed class GetRunCockpitQueryHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetRunCockpitQuery, Result<GetRunCockpitQueryResult>>
{
    private static readonly RunStage[] StageSequence =
    [
        RunStage.Intake,
        RunStage.Plan,
        RunStage.Critique,
        RunStage.Resolution,
        RunStage.Execute,
        RunStage.Completed,
    ];

    public async Task<Result<GetRunCockpitQueryResult>> HandleAsync(
        GetRunCockpitQuery query,
        CancellationToken cancellationToken)
    {
        var projection = await dbContext.Runs
            .AsNoTracking()
            .Where(run => run.Id == query.RunId)
            .Join(
                dbContext.Projects.AsNoTracking(),
                run => run.ProjectId,
                project => project.Id,
                (run, project) => new { Run = run, ProjectName = project.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (projection is null)
        {
            return Result<GetRunCockpitQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var run = projection.Run;
        var latestSequence = await dbContext.Events
            .AsNoTracking()
            .Where(runEvent => runEvent.RunId == run.Id)
            .Select(runEvent => (long?)runEvent.Sequence)
            .MaxAsync(cancellationToken) ?? 0;

        var autonomousDurationSeconds = run.AccumulatedAutonomousSeconds;
        if (run.Lifecycle == RunLifecycle.Running)
        {
            autonomousDurationSeconds += (timeProvider.GetUtcNow() - run.LastAdvancedAtUtc).TotalSeconds;
        }

        var latestAgentAttempt = await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.RunId == run.Id && attempt.Kind == AttemptKind.Agent)
            .OrderByDescending(attempt => attempt.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var stageMap = StageSequence
            .Select(stage => new RunCockpitStageEntry(stage, IsCompleted: stage < run.Stage, IsActive: stage == run.Stage))
            .ToArray();

        return Result<GetRunCockpitQueryResult>.Success(
            new GetRunCockpitQueryResult(
                run.Id,
                run.ProjectId,
                projection.ProjectName,
                run.ExecutionNumber,
                run.Objective,
                run.Lifecycle,
                run.Stage,
                run.ActiveParticipant,
                autonomousDurationSeconds,
                latestSequence,
                stageMap,
                CanPause: false,
                CanStop: false,
                latestAgentAttempt is null
                    ? null
                    : new RunCockpitAgentAttemptEntry(
                        latestAgentAttempt.Id,
                        latestAgentAttempt.AttemptNumber,
                        latestAgentAttempt.AgentRole,
                        latestAgentAttempt.AgentProvider,
                        latestAgentAttempt.Status,
                        latestAgentAttempt.AgentOutcome,
                        latestAgentAttempt.AgentDispatchedAtUtc,
                        latestAgentAttempt.GetAgentProcessExecutionEvidence(),
                        latestAgentAttempt.AgentTimeout)));
    }
}
