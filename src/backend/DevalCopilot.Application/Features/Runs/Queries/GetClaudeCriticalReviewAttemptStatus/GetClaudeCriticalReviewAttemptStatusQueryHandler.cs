using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetClaudeCriticalReviewAttemptStatus;

public sealed class GetClaudeCriticalReviewAttemptStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetClaudeCriticalReviewAttemptStatusQuery, Result<ClaudeCriticalReviewAttemptStatusQueryResult>>
{
    public async Task<Result<ClaudeCriticalReviewAttemptStatusQueryResult>> HandleAsync(
        GetClaudeCriticalReviewAttemptStatusQuery query, CancellationToken cancellationToken)
    {
        var runExists = await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken);
        if (!runExists)
        {
            return Result<ClaudeCriticalReviewAttemptStatusQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var attempt = await dbContext.Attempts
            .AsNoTracking()
            .Where(candidate =>
                candidate.RunId == query.RunId && candidate.Kind == AttemptKind.Agent && candidate.AgentRole == AgentRole.CriticalReviewer)
            .OrderByDescending(candidate => candidate.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (attempt is null)
        {
            return Result<ClaudeCriticalReviewAttemptStatusQueryResult>.Success(ClaudeCriticalReviewAttemptStatusQueryResult.NoAttempt);
        }

        var artifacts = await dbContext.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.AttemptId == attempt.Id)
            .Select(artifact => new AgentAttemptArtifactMetadata(artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome))
            .ToArrayAsync(cancellationToken);

        return Result<ClaudeCriticalReviewAttemptStatusQueryResult>.Success(new ClaudeCriticalReviewAttemptStatusQueryResult(
            true,
            attempt.Id,
            attempt.AttemptNumber,
            attempt.AgentInputCollaborationMessageId,
            attempt.Status,
            attempt.AgentOutcome,
            attempt.ClaimedAtUtc,
            attempt.AgentDispatchedAtUtc,
            attempt.CompletedAtUtc,
            artifacts));
    }
}
