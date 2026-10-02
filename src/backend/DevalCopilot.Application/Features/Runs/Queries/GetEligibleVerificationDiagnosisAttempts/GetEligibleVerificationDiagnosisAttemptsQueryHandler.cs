using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleVerificationDiagnosisAttempts;

/// <summary>The eligibility feed for <c>VerificationDiagnosisSupervisor</c>, restricted to the coherent Codex + CodeReviewer +
/// VerificationDiagnosis tuple, so an ordinary implementation-review attempt is never handed to it and a diagnosis is never
/// handed to the ordinary review supervisor.</summary>
public sealed class GetEligibleVerificationDiagnosisAttemptsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetEligibleVerificationDiagnosisAttemptsQuery, IReadOnlyList<EligibleVerificationDiagnosisAttempt>>
{
    public async Task<IReadOnlyList<EligibleVerificationDiagnosisAttempt>> HandleAsync(
        GetEligibleVerificationDiagnosisAttemptsQuery query, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.Kind == AttemptKind.Agent
                && attempt.AgentProvider == AgentProvider.Codex
                && attempt.AgentRole == AgentRole.CodeReviewer
                && attempt.AgentResponseContract == AgentResponseContract.VerificationDiagnosis
                && attempt.AgentPermissionProfile == AgentPermissionProfile.ReadOnly
                && attempt.AgentAdapterContractVersion == VerificationDiagnosisPolicy.AdapterContractVersion
                && attempt.Status == AttemptStatus.Running
                && attempt.AgentDispatchedAtUtc == null)
            .Join(
                dbContext.Runs.AsNoTracking().Where(run => run.Lifecycle == RunLifecycle.Running && (EF.Property<string>(run, Run.ExecutionModeStorageProperty) == RunExecutionModeStorage.ManualAgentText || EF.Property<string>(run, Run.ExecutionModeStorageProperty) == RunExecutionModeStorage.LegacyText)),
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
                    combined.attempt.AgentRequestedModel,
                    combined.attempt.AgentRequestedEffort,
                })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        var workspaceIds = candidates.Select(candidate => candidate.GitWorkspaceId).Distinct().ToArray();
        var checkpoints = await dbContext.GitCheckpoints
            .AsNoTracking()
            .Where(checkpoint => workspaceIds.Contains(checkpoint.WorkspaceId))
            .Select(checkpoint => new { checkpoint.WorkspaceId, checkpoint.Id, checkpoint.CheckpointNumber })
            .ToListAsync(cancellationToken);
        var currentCheckpointByWorkspace = checkpoints
            .GroupBy(checkpoint => checkpoint.WorkspaceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(checkpoint => checkpoint.CheckpointNumber).First().Id);

        return candidates
            .Where(candidate =>
                currentCheckpointByWorkspace.TryGetValue(candidate.GitWorkspaceId, out var currentCheckpointId)
                && currentCheckpointId == candidate.GitCheckpointId)
            .OrderBy(candidate => candidate.ClaimedAtUtc)
            .Select(candidate => new EligibleVerificationDiagnosisAttempt(
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
                candidate.AgentRequestedModel,
                candidate.AgentRequestedEffort))
            .ToArray();
    }
}
