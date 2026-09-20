using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleReviewCorrectionAttempts;

public sealed class GetEligibleReviewCorrectionAttemptsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetEligibleReviewCorrectionAttemptsQuery, IReadOnlyList<EligibleReviewCorrectionAttempt>>
{
    public async Task<IReadOnlyList<EligibleReviewCorrectionAttempt>> HandleAsync(
        GetEligibleReviewCorrectionAttemptsQuery query, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.Attempts.AsNoTracking()
            .Where(attempt =>
                attempt.Kind == AttemptKind.Agent
                && attempt.AgentRole == AgentRole.Implementer
                && attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection
                && attempt.Status == AttemptStatus.Running
                && attempt.AgentDispatchedAtUtc == null)
            .Join(dbContext.Runs.AsNoTracking().Where(run => run.Lifecycle == RunLifecycle.Running), attempt => attempt.RunId, run => run.Id, (attempt, _) => attempt)
            .Join(dbContext.GitWorkspaces.AsNoTracking().Where(workspace => workspace.Status == WorkspaceStatus.Ready), attempt => attempt.AgentGitWorkspaceId!.Value, workspace => workspace.Id, (attempt, workspace) => new { attempt, workspace })
            .Join(dbContext.RepositoryMutationLeases.AsNoTracking().Where(lease => lease.Status == LeaseStatus.Active), combined => combined.workspace.Id, lease => lease.WorkspaceId, (combined, _) => combined)
            .Join(dbContext.Artifacts.AsNoTracking()
                    .Where(artifact => artifact.Purpose == ArtifactPurpose.AgentContextManifest
                        && artifact.CaptureOutcome == ArtifactCaptureOutcome.Captured),
                combined => combined.attempt.AgentContextManifestArtifactId!.Value,
                artifact => artifact.Id,
                (combined, artifact) => new
                {
                    combined.attempt.Id,
                    combined.attempt.RunId,
                    combined.attempt.ClaimedAtUtc,
                    GitWorkspaceId = combined.workspace.Id,
                    combined.workspace.WorkspacePath,
                    GitCheckpointId = combined.attempt.AgentGitCheckpointId!.Value,
                    CheckpointFingerprintSha256 = combined.attempt.AgentCheckpointFingerprintSha256!,
                    ContextManifestRelativeStoragePath = artifact.RelativeStoragePath,
                    ContextManifestByteLength = artifact.ByteLength,
                    ContextManifestContentHash = artifact.ContentHash,
                    Timeout = combined.attempt.AgentTimeout!.Value,
                    MaxBytesPerStream = combined.attempt.AgentMaxBytesPerStream!.Value,
                    MaxTotalCapturedBytes = combined.attempt.AgentMaxTotalCapturedBytes!.Value,
                })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        var workspaceIds = candidates.Select(candidate => candidate.GitWorkspaceId).Distinct().ToArray();
        var checkpoints = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(checkpoint => workspaceIds.Contains(checkpoint.WorkspaceId))
            .Select(checkpoint => new { checkpoint.WorkspaceId, checkpoint.Id, checkpoint.CheckpointNumber })
            .ToListAsync(cancellationToken);
        var current = checkpoints.GroupBy(checkpoint => checkpoint.WorkspaceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.CheckpointNumber).First().Id);

        var eligible = candidates.Where(candidate => current.TryGetValue(candidate.GitWorkspaceId, out var id) && id == candidate.GitCheckpointId)
            .OrderBy(candidate => candidate.ClaimedAtUtc)
            .Select(candidate => new EligibleReviewCorrectionAttempt(
                candidate.Id, candidate.RunId, candidate.GitWorkspaceId, candidate.WorkspacePath,
                candidate.GitCheckpointId, candidate.CheckpointFingerprintSha256,
                candidate.ContextManifestRelativeStoragePath, candidate.ContextManifestByteLength,
                candidate.ContextManifestContentHash, candidate.Timeout, candidate.MaxBytesPerStream,
                candidate.MaxTotalCapturedBytes, []))
            .ToArray();

        var attemptIds = eligible.Select(candidate => candidate.AttemptId).ToArray();
        var inputRows = await dbContext.AttemptInputMessages.AsNoTracking()
            .Where(input => attemptIds.Contains(input.AttemptId))
            .OrderBy(input => input.AttemptId)
            .ThenBy(input => input.Sequence)
            .Select(input => new { input.AttemptId, input.CollaborationMessageId })
            .ToListAsync(cancellationToken);
        var inputByAttempt = inputRows.GroupBy(row => row.AttemptId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Skip(1).Select(row => row.CollaborationMessageId).ToArray());
        return eligible.Select(candidate => candidate with
        {
            OrderedInputMessageIds = inputByAttempt.TryGetValue(candidate.AttemptId, out var ids) ? ids : [],
        }).ToArray();
    }
}
