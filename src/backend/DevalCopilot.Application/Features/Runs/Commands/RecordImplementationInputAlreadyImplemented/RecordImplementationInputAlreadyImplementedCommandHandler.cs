using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationInputAlreadyImplemented;

public sealed class RecordImplementationInputAlreadyImplementedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordImplementationInputAlreadyImplementedCommand, Result>
{
    public async Task<Result> HandleAsync(RecordImplementationInputAlreadyImplementedCommand command, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (attempt is null || attempt.RunId != command.RunId)
        {
            return Result.Failure(Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Agent
            || attempt.AgentRole != AgentRole.Implementer
            || attempt.AgentResponseContract != AgentAttemptContract.For(AgentRole.Implementer).ResponseContract)
        {
            return Result.Failure(Error.Conflict("attempts.not_implementation", "The attempt is not an implementation attempt."));
        }

        if (attempt.Status != AttemptStatus.Running || attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result.Failure(
                Error.Conflict(
                    "attempts.not_eligible",
                    "Only an undispatched, running implementation attempt can record input-already-implemented."));
        }

        // Never trusted from the caller — re-verified fresh here, mirroring
        // RecordChallengeResolutionInputAlreadyResolvedCommandHandler's own reasoning exactly.
        var planProposalMessageId = await ImplementationInputIdentity.GetPlanProposalMessageIdAsync(dbContext, attempt.Id, cancellationToken);
        var competingImplementationExists = await ImplementationInputIdentity.HasCompetingSuccessfulImplementationAsync(
            dbContext, attempt.RunId, attempt.Id, planProposalMessageId, attempt.AgentGitCheckpointId!.Value, cancellationToken);

        if (!competingImplementationExists)
        {
            return Result.Failure(
                Error.Conflict(
                    "attempts.no_competing_implementation_found",
                    "No other completed successful implementation was found for this attempt's exact plan and checkpoint."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        attempt.CompleteImplementation(AgentOutcome.InputAlreadyImplemented, resultGitCheckpointId: null, nowUtc);

        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(),
            command.RunId,
            command.AttemptId,
            RunEventType.AgentAttemptCompleted,
            ParticipantKind.Orchestrator,
            JsonSerializer.Serialize(new { status = attempt.Status.ToString(), outcome = attempt.AgentOutcome.ToString() }),
            nowUtc));

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
