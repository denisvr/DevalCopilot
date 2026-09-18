using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleChallengeResolutionAttempts;

/// <summary>Mirrors <c>GetEligibleClaudeCriticalReviewAttemptsQueryHandler</c> exactly, restricted
/// to Codex + Resolver attempts — the entirely separate eligibility feed for
/// <c>ChallengeResolutionSupervisor</c>, so a claimed Codex planning attempt is never handed to it
/// and vice versa, even though both share the Codex provider.</summary>
public sealed class GetEligibleChallengeResolutionAttemptsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetEligibleChallengeResolutionAttemptsQuery, IReadOnlyList<EligibleChallengeResolutionAttempt>>
{
    public async Task<IReadOnlyList<EligibleChallengeResolutionAttempt>> HandleAsync(
        GetEligibleChallengeResolutionAttemptsQuery query, CancellationToken cancellationToken)
    {
        // Same client-side ordering rationale as GetEligibleClaudeCriticalReviewAttemptsQueryHandler:
        // SQLite cannot translate ORDER BY over DateTimeOffset server-side, and the eligible set
        // is always small.
        var candidates = await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.Kind == AttemptKind.Agent
                && attempt.AgentProvider == AgentProvider.Codex
                && attempt.AgentRole == AgentRole.Resolver
                && attempt.Status == AttemptStatus.Running
                && attempt.AgentDispatchedAtUtc == null)
            .Join(
                dbContext.Runs.AsNoTracking().Where(run => run.Lifecycle == RunLifecycle.Running),
                attempt => attempt.RunId,
                run => run.Id,
                (attempt, run) => attempt)
            .Join(
                dbContext.GitWorkspaces.AsNoTracking().Where(workspace => workspace.Status == WorkspaceStatus.Ready),
                attempt => attempt.AgentGitWorkspaceId!.Value,
                workspace => workspace.Id,
                (attempt, workspace) => new { attempt, workspace })
            .Join(
                dbContext.RepositoryMutationLeases.AsNoTracking().Where(lease => lease.Status == LeaseStatus.Active),
                combined => combined.workspace.Id,
                lease => lease.WorkspaceId,
                (combined, lease) => combined)
            .Join(
                dbContext.Artifacts.AsNoTracking(),
                combined => combined.attempt.AgentContextManifestArtifactId!.Value,
                artifact => artifact.Id,
                (combined, artifact) => new
                {
                    combined.attempt.Id,
                    combined.attempt.RunId,
                    combined.attempt.ClaimedAtUtc,
                    GitWorkspaceId = combined.attempt.AgentGitWorkspaceId!.Value,
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

        var attemptIds = candidates.Select(candidate => candidate.Id).ToArray();
        var orderedInputMessagesByAttempt = await dbContext.AttemptInputMessages
            .AsNoTracking()
            .Where(inputMessage => attemptIds.Contains(inputMessage.AttemptId))
            .OrderBy(inputMessage => inputMessage.Sequence)
            .Select(inputMessage => new { inputMessage.AttemptId, inputMessage.Sequence, inputMessage.CollaborationMessageId })
            .ToListAsync(cancellationToken);
        var inputMessagesByAttempt = orderedInputMessagesByAttempt
            .GroupBy(inputMessage => inputMessage.AttemptId)
            .ToDictionary(group => group.Key, group => group.OrderBy(inputMessage => inputMessage.Sequence).ToList());

        var currentCheckpointByWorkspace = await GetCurrentCheckpointByWorkspaceAsync(
            candidates.Select(candidate => candidate.GitWorkspaceId), cancellationToken);

        return candidates
            .Where(candidate =>
                currentCheckpointByWorkspace.TryGetValue(candidate.GitWorkspaceId, out var currentCheckpointId)
                && currentCheckpointId == candidate.GitCheckpointId
                && inputMessagesByAttempt.ContainsKey(candidate.Id))
            .OrderBy(candidate => candidate.ClaimedAtUtc)
            .Select(candidate =>
            {
                var orderedInputMessages = inputMessagesByAttempt[candidate.Id];
                return new EligibleChallengeResolutionAttempt(
                    candidate.Id,
                    candidate.RunId,
                    candidate.GitWorkspaceId,
                    candidate.WorkspacePath,
                    candidate.GitCheckpointId,
                    candidate.CheckpointFingerprintSha256,
                    candidate.ContextManifestRelativeStoragePath,
                    candidate.ContextManifestByteLength,
                    candidate.ContextManifestContentHash,
                    candidate.Timeout,
                    candidate.MaxBytesPerStream,
                    candidate.MaxTotalCapturedBytes,
                    orderedInputMessages.Single(inputMessage => inputMessage.Sequence == 0).CollaborationMessageId,
                    orderedInputMessages
                        .Where(inputMessage => inputMessage.Sequence > 0)
                        .Select(inputMessage => inputMessage.CollaborationMessageId)
                        .ToArray());
            })
            .ToArray();
    }

    private async Task<Dictionary<Guid, Guid>> GetCurrentCheckpointByWorkspaceAsync(
        IEnumerable<Guid> workspaceIds, CancellationToken cancellationToken)
    {
        var ids = workspaceIds.Distinct().ToArray();
        var checkpoints = await dbContext.GitCheckpoints
            .AsNoTracking()
            .Where(checkpoint => ids.Contains(checkpoint.WorkspaceId))
            .Select(checkpoint => new { checkpoint.WorkspaceId, checkpoint.Id, checkpoint.CheckpointNumber })
            .ToListAsync(cancellationToken);

        return checkpoints
            .GroupBy(checkpoint => checkpoint.WorkspaceId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(checkpoint => checkpoint.CheckpointNumber).First().Id);
    }
}
