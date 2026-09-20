using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordCodeReviewInputAlreadyCodeReviewed;

public sealed class RecordCodeReviewInputAlreadyCodeReviewedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordCodeReviewInputAlreadyCodeReviewedCommand, Result>
{
    public async Task<Result> HandleAsync(RecordCodeReviewInputAlreadyCodeReviewedCommand command, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (attempt is null || attempt.RunId != command.RunId)
        {
            return Result.Failure(Error.NotFound("attempts.not_found", "The requested attempt was not found for this run."));
        }

        if (attempt.Kind != AttemptKind.Agent
            || attempt.AgentRole != AgentRole.CodeReviewer
            || attempt.AgentResponseContract != AgentAttemptContract.For(AgentRole.CodeReviewer).ResponseContract)
        {
            return Result.Failure(Error.Conflict("attempts.not_code_review", "The attempt is not a code-review attempt."));
        }

        if (attempt.Status != AttemptStatus.Running || attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result.Failure(
                Error.Conflict(
                    "attempts.not_eligible",
                    "Only an undispatched, running code-review attempt can record input-already-code-reviewed."));
        }

        // Never trusted from the caller: independently re-read and re-verify this attempt's own
        // exact input identity before ever recording that a competing review exists.
        var executionReportMessageId = await CodeReviewInputIdentity.GetExecutionReportMessageIdAsync(dbContext, attempt.Id, cancellationToken);
        var orderedVerificationExecutionIds = await CodeReviewInputIdentity.GetOrderedVerificationExecutionIdsAsync(
            dbContext, attempt.Id, cancellationToken);
        var competingReviewExists = await CodeReviewInputIdentity.HasCompetingSuccessfulReviewAsync(
            dbContext, attempt.RunId, attempt.Id, executionReportMessageId, orderedVerificationExecutionIds, cancellationToken);

        if (!competingReviewExists)
        {
            return Result.Failure(
                Error.Conflict(
                    "attempts.no_competing_review_found",
                    "No other completed successful code review was found for this attempt's exact input identity."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        attempt.CompleteAgent(AgentOutcome.InputAlreadyCodeReviewed, completionFingerprintSha256: null, nowUtc);

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
