using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewInputAlreadyReviewed;

public sealed class RecordClaudeCriticalReviewInputAlreadyReviewedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordClaudeCriticalReviewInputAlreadyReviewedCommand, Result>
{
    public async Task<Result> HandleAsync(RecordClaudeCriticalReviewInputAlreadyReviewedCommand command, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (attempt is null || attempt.RunId != command.RunId)
        {
            return Result.Failure(Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Agent
            || attempt.AgentRole != AgentRole.CriticalReviewer
            || attempt.AgentResponseContract != AgentResponseContract.CriticalReview)
        {
            return Result.Failure(Error.Conflict("attempts.not_critical_review", "The attempt is not a critical-review attempt."));
        }

        if (attempt.Status != AttemptStatus.Running || attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result.Failure(
                Error.Conflict(
                    "attempts.not_eligible",
                    "Only an undispatched, running critical-review attempt can record input-already-reviewed."));
        }

        // Never trusted from the caller: this is the one fact this command exists to guarantee is
        // real, so it is re-verified fresh here rather than accepted on the strength of whatever
        // earlier check (e.g. MarkAgentAttemptDispatchedCommand's own last-gate check) led the
        // caller to dispatch this command in the first place.
        var inputMessageId = await dbContext.AttemptInputMessages
            .Where(inputMessage => inputMessage.AttemptId == attempt.Id && inputMessage.Sequence == 0)
            .Select(inputMessage => inputMessage.CollaborationMessageId)
            .SingleAsync(cancellationToken);

        var competingReviewExists = await dbContext.Attempts
            .Join(
                dbContext.AttemptInputMessages.Where(
                    inputMessage => inputMessage.CollaborationMessageId == inputMessageId && inputMessage.Sequence == 0),
                candidate => candidate.Id,
                inputMessage => inputMessage.AttemptId,
                (candidate, inputMessage) => candidate)
            .AnyAsync(
                candidate =>
                    candidate.Id != attempt.Id
                    && candidate.Kind == AttemptKind.Agent
                    && candidate.AgentRole == AgentRole.CriticalReviewer
                    && candidate.Status == AttemptStatus.Completed
                    && (candidate.AgentOutcome == AgentOutcome.Accepted || candidate.AgentOutcome == AgentOutcome.Challenged),
                cancellationToken);

        if (!competingReviewExists)
        {
            // The evidence this command exists to record does not actually hold — nothing is
            // mutated. A caller that dispatches this command speculatively, or loses its own race
            // against this re-check, gets a safe, truthful rejection instead of an invented fact.
            return Result.Failure(
                Error.Conflict(
                    "attempts.no_competing_review_found",
                    "No other completed successful critical review was found for this attempt's input proposal."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        attempt.CompleteAgent(AgentOutcome.InputAlreadyReviewed, completionFingerprintSha256: null, nowUtc);

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
