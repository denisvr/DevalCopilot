using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCodeReviewAttemptStatus;

public sealed class GetCodeReviewAttemptStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetCodeReviewAttemptStatusQuery, Result<CodeReviewAttemptStatusQueryResult>>
{
    public async Task<Result<CodeReviewAttemptStatusQueryResult>> HandleAsync(
        GetCodeReviewAttemptStatusQuery query, CancellationToken cancellationToken)
    {
        var runExists = await dbContext.Runs.AsNoTracking().AnyAsync(run => run.Id == query.RunId, cancellationToken);
        if (!runExists)
        {
            return Result<CodeReviewAttemptStatusQueryResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        var attempt = await dbContext.Attempts
            .AsNoTracking()
            .Where(candidate =>
                candidate.RunId == query.RunId && candidate.Kind == AttemptKind.Agent && candidate.AgentRole == AgentRole.CodeReviewer)
            .OrderByDescending(candidate => candidate.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (attempt is null)
        {
            return Result<CodeReviewAttemptStatusQueryResult>.Success(CodeReviewAttemptStatusQueryResult.NoAttempt);
        }

        var artifacts = await dbContext.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.AttemptId == attempt.Id)
            .Select(artifact => new AgentAttemptArtifactMetadata(artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome))
            .ToArrayAsync(cancellationToken);

        var executionReportMessageId = await dbContext.AttemptInputMessages
            .AsNoTracking()
            .Where(inputMessage => inputMessage.AttemptId == attempt.Id && inputMessage.Sequence == 0)
            .Select(inputMessage => (Guid?)inputMessage.CollaborationMessageId)
            .SingleOrDefaultAsync(cancellationToken);

        return Result<CodeReviewAttemptStatusQueryResult>.Success(new CodeReviewAttemptStatusQueryResult(
            true,
            attempt.Id,
            attempt.AttemptNumber,
            executionReportMessageId,
            attempt.Status,
            attempt.AgentOutcome,
            attempt.ClaimedAtUtc,
            attempt.AgentDispatchedAtUtc,
            attempt.CompletedAtUtc,
            artifacts,
            attempt.GetAgentProcessExecutionEvidence(),
            attempt.AgentTimeout));
    }
}
