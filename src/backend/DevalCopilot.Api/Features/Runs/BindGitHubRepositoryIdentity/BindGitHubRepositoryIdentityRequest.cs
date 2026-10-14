namespace DevalCopilot.Api.Features.Runs.BindGitHubRepositoryIdentity;

/// <summary>The only caller-supplied facts of an explicit repository identity binding: the completed local-commit operation, its exact
/// lowercase 40-character commit, the recorded target and the opaque repository node identifier the Human confirmed. There is no owner,
/// repository, branch, URL, executable or observation time: the host resolves the scope from pinned rows and observes the identity itself.
/// Unknown and duplicate members are refused before anything is dispatched.</summary>
public sealed record BindGitHubRepositoryIdentityRequest
{
    public required Guid OperationId { get; init; }

    public required string CommitSha { get; init; }

    public required Guid TargetId { get; init; }

    public required string RepositoryNodeId { get; init; }
}
