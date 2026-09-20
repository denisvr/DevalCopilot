using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionInputAlreadyResolved;

public sealed class RecordChallengeResolutionInputAlreadyResolvedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordChallengeResolutionInputAlreadyResolvedCommand, Result>
{
    public async Task<Result> HandleAsync(RecordChallengeResolutionInputAlreadyResolvedCommand command, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (attempt is null || attempt.RunId != command.RunId)
        {
            return Result.Failure(Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Agent
            || attempt.AgentRole != AgentRole.Resolver
            || attempt.AgentResponseContract != AgentResponseContract.ChallengeResolution)
        {
            return Result.Failure(Error.Conflict("attempts.not_challenge_resolution", "The attempt is not a challenge-resolution attempt."));
        }

        if (attempt.Status != AttemptStatus.Running || attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result.Failure(
                Error.Conflict(
                    "attempts.not_eligible",
                    "Only an undispatched, running challenge-resolution attempt can record input-already-resolved."));
        }

        // Never trusted from the caller: this is the one fact this command exists to guarantee is
        // real, so it is re-verified fresh here rather than accepted on the strength of whatever
        // earlier check (e.g. MarkAgentAttemptDispatchedCommand's own last-gate check) led the
        // caller to dispatch this command in the first place.
        var orderedInputMessageIds = await ChallengeResolutionInputIdentity.GetOrderedInputMessageIdsAsync(
            dbContext, attempt.Id, cancellationToken);
        var competingResolutionExists = await ChallengeResolutionInputIdentity.HasCompetingExactResolutionAsync(
            dbContext, attempt.RunId, attempt.Id, orderedInputMessageIds, cancellationToken);

        if (!competingResolutionExists)
        {
            // The evidence this command exists to record does not actually hold — nothing is
            // mutated. A caller that dispatches this command speculatively, or loses its own race
            // against this re-check, gets a safe, truthful rejection instead of an invented fact.
            return Result.Failure(
                Error.Conflict(
                    "attempts.no_competing_resolution_found",
                    "No other completed successful resolution was found for this attempt's exact input set."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        attempt.CompleteAgent(AgentOutcome.InputAlreadyResolved, completionFingerprintSha256: null, nowUtc);

        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(),
            command.RunId,
            command.AttemptId,
            RunEventType.AgentAttemptCompleted,
            ParticipantIdentity.ForOrchestrator(),
            JsonSerializer.Serialize(new { status = attempt.Status.ToString(), outcome = attempt.AgentOutcome.ToString() }),
            nowUtc));

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
