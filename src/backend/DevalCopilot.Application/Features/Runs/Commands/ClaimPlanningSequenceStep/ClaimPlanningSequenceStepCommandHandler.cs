using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ClaimPlanningSequenceStep;

public sealed class ClaimPlanningSequenceStepCommandHandler(IDevalCopilotDbContext dbContext, IApplicationMediator mediator)
    : ICommandHandler<ClaimPlanningSequenceStepCommand, Result<ClaimPlanningSequenceStepCommandResult>>
{
    public async Task<Result<ClaimPlanningSequenceStepCommandResult>> HandleAsync(
        ClaimPlanningSequenceStepCommand command, CancellationToken cancellationToken)
    {
        var ticket = command.Ticket;

        // Only the run identity is read here, untracked; the composed claim decides everything else from its own fresh reads and
        // re-checks the exact step at its durable seam.
        var runId = await dbContext.PlanningSequences.AsNoTracking()
            .Where(candidate => candidate.Id == ticket.SequenceId)
            .Select(candidate => (Guid?)candidate.RunId)
            .SingleOrDefaultAsync(cancellationToken);
        if (runId is not { } run)
        {
            return Result<ClaimPlanningSequenceStepCommandResult>.Failure(PlanningSequenceErrors.StepAuthorityLost());
        }

        // The step's fixed role selects exactly one of the three existing claim commands; nothing here is caller-supplied.
        switch (PlanningSequencePolicy.RoleOf(ticket.Ordinal))
        {
            case AgentRole.Planner:
                {
                    var planned = await mediator.SendAsync(
                        new CreateCodexPlanningAttemptCommand(run) { PlanningStep = ticket }, cancellationToken);
                    return planned.IsFailure
                        ? Result<ClaimPlanningSequenceStepCommandResult>.Failure(planned.Errors)
                        : Result<ClaimPlanningSequenceStepCommandResult>.Success(
                            new ClaimPlanningSequenceStepCommandResult(planned.Value.AttemptId, planned.Value.AttemptNumber));
                }

            case AgentRole.CriticalReviewer:
                {
                    var reviewed = await mediator.SendAsync(
                        new CreateClaudeCriticalReviewAttemptCommand(run, ticket.InputProposalMessageId) { PlanningStep = ticket },
                        cancellationToken);
                    return reviewed.IsFailure
                        ? Result<ClaimPlanningSequenceStepCommandResult>.Failure(reviewed.Errors)
                        : Result<ClaimPlanningSequenceStepCommandResult>.Success(
                            new ClaimPlanningSequenceStepCommandResult(reviewed.Value.AttemptId, reviewed.Value.AttemptNumber));
                }

            default:
                {
                    var resolved = await mediator.SendAsync(
                        new CreateChallengeResolutionAttemptCommand(run, ticket.PredecessorAttemptId) { PlanningStep = ticket },
                        cancellationToken);
                    return resolved.IsFailure
                        ? Result<ClaimPlanningSequenceStepCommandResult>.Failure(resolved.Errors)
                        : Result<ClaimPlanningSequenceStepCommandResult>.Success(
                            new ClaimPlanningSequenceStepCommandResult(resolved.Value.AttemptId, resolved.Value.AttemptNumber));
                }
        }
    }
}
