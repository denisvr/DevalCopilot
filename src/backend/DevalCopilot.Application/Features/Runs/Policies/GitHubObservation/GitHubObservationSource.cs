namespace DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

/// <summary>The pinned facts a target is recorded against: the run, its project, the completed delivery operation, its workspace and
/// the exact recorded commit and branch.</summary>
internal sealed record GitHubObservationSource(
    Guid RunId, Guid ProjectId, Guid OperationId, Guid WorkspaceId, string CommitSha, string BranchName)
{
    public GitHubObservationSourceView ToView() => new(OperationId, CommitSha, BranchName);
}
