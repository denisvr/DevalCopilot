using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleAgentAttempts;

public sealed class GetEligibleAgentAttemptsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetEligibleAgentAttemptsQuery, IReadOnlyList<EligibleAgentAttempt>>
{
    public async Task<IReadOnlyList<EligibleAgentAttempt>> HandleAsync(
        GetEligibleAgentAttemptsQuery query, CancellationToken cancellationToken)
    {
        // Same client-side ordering rationale as GetEligibleProcessAttemptsQueryHandler: SQLite
        // cannot translate ORDER BY over DateTimeOffset server-side, and the eligible set is
        // always small.
        //
        // Joined against Runs (Lifecycle == Running), a Ready GitWorkspace, and an Active
        // RepositoryMutationLease for that workspace — an attempt whose run, workspace, or lease
        // is missing or no longer eligible is never handed to the supervisor: zero provider
        // invocations for it. The workspace's current checkpoint is checked separately below
        // (a correlated "current per workspace" comparison is awkward to express as a single
        // translatable join). Joined against Artifacts for the sealed context-manifest metadata
        // recorded at claim time.
        // Codex-only: this handler feeds AgentAttemptSupervisor, which only ever knows how to
        // invoke ICodexPlanningAdapter. A ClaudeCode critical-review attempt is claimed and
        // dispatched by the entirely separate ClaudeCriticalReviewSupervisor/
        // GetEligibleClaudeCriticalReviewAttemptsQuery pair — without this filter, a claimed
        // critical-review attempt would be handed to this supervisor and it would try to invoke
        // the Codex CLI against it.
        var candidates = await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.Kind == AttemptKind.Agent
                && attempt.AgentProvider == AgentProvider.Codex
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

        var currentCheckpointByWorkspace = await GetCurrentCheckpointByWorkspaceAsync(
            candidates.Select(candidate => candidate.GitWorkspaceId), cancellationToken);

        return candidates
            .Where(candidate =>
                currentCheckpointByWorkspace.TryGetValue(candidate.GitWorkspaceId, out var currentCheckpointId)
                && currentCheckpointId == candidate.GitCheckpointId)
            .OrderBy(candidate => candidate.ClaimedAtUtc)
            .Select(candidate => new EligibleAgentAttempt(
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
                candidate.MaxTotalCapturedBytes))
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
