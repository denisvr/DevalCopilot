using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectCheckpointReviews;

public sealed class GetProjectCheckpointReviewsQueryHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader)
    : IQueryHandler<GetProjectCheckpointReviewsQuery, Result<IReadOnlyList<CheckpointReviewQueryResult>>>
{
    private const int MaximumResults = 50;

    public async Task<Result<IReadOnlyList<CheckpointReviewQueryResult>>> HandleAsync(
        GetProjectCheckpointReviewsQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Projects.AnyAsync(project => project.Id == query.ProjectId, cancellationToken))
        {
            return Result<IReadOnlyList<CheckpointReviewQueryResult>>.Failure(
                Error.NotFound("projects.not_found", "This project does not exist."));
        }

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == query.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        var currentCheckpoint = workspace is null
            ? null
            : await dbContext.GitCheckpoints
                .Where(candidate => candidate.WorkspaceId == workspace.Id)
                .OrderByDescending(candidate => candidate.CheckpointNumber)
                .FirstOrDefaultAsync(cancellationToken);
        string? currentFingerprint = null;
        if (workspace?.Status == WorkspaceStatus.Ready && await dbContext.RepositoryMutationLeases.AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
            if (evidence.Outcome == GitWorkspaceEvidenceOutcome.Success)
            {
                currentFingerprint = evidence.FingerprintSha256;
            }
        }

        var reviews = await dbContext.CheckpointReviews
            .AsNoTracking()
            .Include(review => review.Evidence)
            .Where(review => review.ProjectId == query.ProjectId)
            .OrderByDescending(review => review.RecordedAtUtcTicks)
            .ThenByDescending(review => review.Id)
            .Take(MaximumResults)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<CheckpointReviewQueryResult>>.Success(reviews.Select(review =>
        {
            var isCurrentCheckpoint = currentCheckpoint?.Id == review.GitCheckpointId;
            var isApplicable = isCurrentCheckpoint && currentFingerprint == review.CheckpointFingerprintSha256;
            var staleReasonCode = isApplicable
                ? null
                : currentCheckpoint is null
                    ? CheckpointReviewApplicabilityReasonCodes.NoCurrentCheckpoint
                    : !isCurrentCheckpoint
                        ? CheckpointReviewApplicabilityReasonCodes.NewerCheckpoint
                        : currentFingerprint is null
                            ? CheckpointReviewApplicabilityReasonCodes.CurrentEvidenceUnavailable
                            : CheckpointReviewApplicabilityReasonCodes.SourceChanged;
            return new CheckpointReviewQueryResult(
                review.Id,
                review.GitCheckpointId,
                review.CheckpointNumber,
                review.CheckpointFingerprintSha256,
                review.Evidence
                    .Select(evidence => new CheckpointReviewEvidenceQueryResult(
                        evidence.VerificationCommandId,
                        evidence.VerificationExecutionId,
                        evidence.VerificationExecutionNumber,
                        evidence.VerificationExecutionStatus,
                        evidence.VerificationExecutionOutcome))
                    .ToArray(),
                review.ActorKind,
                review.Decision,
                isApplicable,
                staleReasonCode,
                review.RecordedAtUtc);
        }).ToArray());
    }
}
