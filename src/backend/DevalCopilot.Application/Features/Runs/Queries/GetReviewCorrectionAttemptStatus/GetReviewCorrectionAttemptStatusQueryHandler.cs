using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetReviewCorrectionAttemptStatus;

public sealed class GetReviewCorrectionAttemptStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetReviewCorrectionAttemptStatusQuery, Result<ReviewCorrectionAttemptStatusQueryResult>>
{
    public async Task<Result<ReviewCorrectionAttemptStatusQueryResult>> HandleAsync(
        GetReviewCorrectionAttemptStatusQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken))
        {
            return Result<ReviewCorrectionAttemptStatusQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var attempt = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == query.RunId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentResponseContract == AgentResponseContract.ReviewCorrection)
            .OrderByDescending(candidate => candidate.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (attempt is null)
        {
            return Result<ReviewCorrectionAttemptStatusQueryResult>.Success(ReviewCorrectionAttemptStatusQueryResult.NoAttempt);
        }

        var implementationReviewAttemptId = await dbContext.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == attempt.Id && input.Sequence > 0)
            .OrderBy(input => input.Sequence)
            .Join(
                dbContext.CollaborationMessages.AsNoTracking(),
                input => input.CollaborationMessageId,
                message => message.Id,
                (_, message) => message.AttemptId)
            .FirstOrDefaultAsync(cancellationToken);

        var artifacts = await dbContext.Artifacts.AsNoTracking()
            .Where(artifact => artifact.AttemptId == attempt.Id)
            .Select(artifact => new AgentCorrectionArtifactMetadata(
                artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome))
            .ToArrayAsync(cancellationToken);
        var responseCount = await dbContext.CollaborationMessages.AsNoTracking()
            .CountAsync(message => message.AttemptId == attempt.Id && message.Type == CollaborationMessageType.RevisionResponse, cancellationToken);

        return Result<ReviewCorrectionAttemptStatusQueryResult>.Success(new ReviewCorrectionAttemptStatusQueryResult(
            true, attempt.Id, attempt.AttemptNumber, implementationReviewAttemptId, attempt.Status, attempt.AgentOutcome,
            attempt.AgentGitCheckpointId, attempt.AgentResultGitCheckpointId, responseCount,
            attempt.ClaimedAtUtc, attempt.AgentDispatchedAtUtc, attempt.CompletedAtUtc, artifacts));
    }
}
