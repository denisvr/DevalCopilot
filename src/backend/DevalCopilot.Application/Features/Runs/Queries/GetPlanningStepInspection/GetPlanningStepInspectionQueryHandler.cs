using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningStepInspection;

public sealed class GetPlanningStepInspectionQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetPlanningStepInspectionQuery, Result<GetPlanningStepInspectionQueryResult>>
{
    public async Task<Result<GetPlanningStepInspectionQueryResult>> HandleAsync(
        GetPlanningStepInspectionQuery query, CancellationToken cancellationToken)
    {
        var reading = await PlanningSequenceInspectionReader.ReadAsync(dbContext, query.RunId, cancellationToken);
        if (reading.Kind is PlanningSequenceInspectionReadingKind.RunNotFound or PlanningSequenceInspectionReadingKind.NotRecorded
            || reading.SequenceId is not { } sequenceId
            || !await PlanningSequenceInspectionReader.HasStepAsync(dbContext, query.RunId, query.StepId, cancellationToken))
        {
            return Result<GetPlanningStepInspectionQueryResult>.Failure(PlanningSequenceInspectionErrors.NotFound());
        }

        if (reading.Structure is not { } structure
            || structure.Steps.FirstOrDefault(candidate => candidate.StepId == query.StepId) is not { } step)
        {
            return Refused(query, sequenceId, PlanningStepInspectionStatus.Unavailable);
        }

        if (step.AttemptId is null)
        {
            // The membership read came after the structural recheck, so a successful no-link answer is compared once more: a concurrent
            // transition during it withholds the answer, with no retry.
            return await PlanningSequenceInspectionReader.IsRevisionUnchangedAsync(
                dbContext, sequenceId, structure.Header.Revision, cancellationToken)
                ? Refused(query, sequenceId, PlanningStepInspectionStatus.NoLinkedAttempt)
                : Refused(query, sequenceId, PlanningStepInspectionStatus.Unavailable);
        }

        var agent = await PlanningSequenceInspectionAgentProof.ProveAsync(dbContext, structure, step, cancellationToken);

        // The structure was proved at one sequence revision and the association is proved after it, so the revision is read once more: a
        // concurrent transition during the association proof withholds the whole answer, with no retry.
        if (agent is null
            || !await PlanningSequenceInspectionReader.IsRevisionUnchangedAsync(
                dbContext, sequenceId, structure.Header.Revision, cancellationToken))
        {
            return Refused(query, sequenceId, PlanningStepInspectionStatus.Unavailable);
        }

        return Result<GetPlanningStepInspectionQueryResult>.Success(new GetPlanningStepInspectionQueryResult(
            query.RunId, sequenceId, query.StepId, PlanningStepInspectionStatus.Recorded, step, agent));
    }

    private static Result<GetPlanningStepInspectionQueryResult> Refused(
        GetPlanningStepInspectionQuery query, Guid sequenceId, PlanningStepInspectionStatus status) =>
        Result<GetPlanningStepInspectionQueryResult>.Success(
            GetPlanningStepInspectionQueryResult.Without(query.RunId, sequenceId, query.StepId, status));
}
