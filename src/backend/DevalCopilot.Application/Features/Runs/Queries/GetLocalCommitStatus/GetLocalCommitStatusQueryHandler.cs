using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalCommit;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitStatus;

public sealed class GetLocalCommitStatusQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetLocalCommitStatusQuery, Result<GetLocalCommitStatusQueryResult>>
{
    public const string OperationExistsCode = "local_commit.operation_exists";

    public async Task<Result<GetLocalCommitStatusQueryResult>> HandleAsync(
        GetLocalCommitStatusQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == query.RunId, cancellationToken))
        {
            return Result<GetLocalCommitStatusQueryResult>.Failure(LocalCommitErrors.RunNotFound());
        }

        var latestSequence = await dbContext.Events.AsNoTracking()
            .Where(runEvent => runEvent.RunId == query.RunId)
            .MaxAsync(runEvent => (long?)runEvent.Sequence, cancellationToken) ?? 0;
        var operation = await dbContext.LocalCommitOperations.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == query.RunId, cancellationToken);
        if (operation is not null)
        {
            return Result<GetLocalCommitStatusQueryResult>.Success(new(
                false, OperationExistsCode, operation.GitCheckpointId, operation.CheckpointNumber,
                operation.CodeReviewAttemptId, operation.HumanCheckpointReviewId,
                LocalCommitOperationView.From(operation), latestSequence));
        }

        var read = await new LocalCommitAuthorityReader(dbContext).ReadAsync(
            query.RunId, new LocalCommitAuthorityReader.Selection(null, null, null), WorkspaceStatus.Ready, null, cancellationToken);
        if (read.Authority is not { } authority)
        {
            return Result<GetLocalCommitStatusQueryResult>.Success(new(
                false, read.Error!.Code, read.CandidateCheckpointId, null, null, null, null, latestSequence));
        }

        return Result<GetLocalCommitStatusQueryResult>.Success(new(
            true, null, authority.Checkpoint.Id, authority.Checkpoint.CheckpointNumber, authority.ReviewAttempt.Id,
            authority.HumanReview.Id, null, latestSequence));
    }
}
