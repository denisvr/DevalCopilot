using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionInputAlreadyCorrected;

public sealed class RecordReviewCorrectionInputAlreadyCorrectedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordReviewCorrectionInputAlreadyCorrectedCommand, Result>
{
    public async Task<Result> HandleAsync(
        RecordReviewCorrectionInputAlreadyCorrectedCommand command, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);
        if (attempt is null || attempt.RunId != command.RunId)
        {
            return Result.Failure(Error.NotFound("attempts.not_found", "The requested attempt was not found."));
        }

        if (attempt.Kind != AttemptKind.Agent
            || attempt.AgentRole != AgentRole.Implementer
            || attempt.AgentResponseContract != AgentResponseContract.ReviewCorrection)
        {
            return Result.Failure(Error.Conflict("attempts.not_review_correction", "The attempt is not a review-correction attempt."));
        }

        if (attempt.Status != AttemptStatus.Running || attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result.Failure(Error.Conflict("attempts.not_eligible", "Only an undispatched running correction can record this outcome."));
        }

        var orderedInputMessageIds = await ReviewCorrectionInputIdentity.GetOrderedInputMessageIdsAsync(
            dbContext, attempt.Id, cancellationToken);
        if (!await ReviewCorrectionInputIdentity.HasCompetingSuccessfulCorrectionAsync(
                dbContext,
                attempt.RunId,
                attempt.Id,
                attempt.AgentGitCheckpointId!.Value,
                orderedInputMessageIds,
                cancellationToken))
        {
            return Result.Failure(Error.Conflict(
                "attempts.no_competing_correction_found",
                "No competing successful correction was found for this exact input identity."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        attempt.CompleteReviewCorrection(AgentOutcome.InputAlreadyCorrected, null, nowUtc);
        dbContext.Events.Add(RunEvent.Record(
            Guid.NewGuid(), command.RunId, command.AttemptId, RunEventType.AgentAttemptCompleted,
            ParticipantIdentity.ForOrchestrator(),
            JsonSerializer.Serialize(new { status = attempt.Status.ToString(), outcome = attempt.AgentOutcome.ToString() }), nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
