namespace DevalCopilot.Api.Features.Runs.RecordGitHubBranchPublicationApproval;

/// <summary>The only caller-supplied facts of an explicit publication approval: its own approval identifier and the completed local-commit
/// operation, exact lowercase 40-character commit, exact recorded branch, target, binding and opaque repository node identifier the Human
/// acknowledged. There is no host, owner, repository, URL, refspec, credential, message, action, text or timestamp: the host resolves the
/// scope from pinned rows and fixes the contract. Unknown, duplicate, differently cased, escaped and mistyped members are refused before
/// anything is dispatched.</summary>
public sealed record RecordGitHubBranchPublicationApprovalRequest
{
    public required Guid ApprovalId { get; init; }

    public required Guid OperationId { get; init; }

    public required string CommitSha { get; init; }

    public required string BranchName { get; init; }

    public required Guid TargetId { get; init; }

    public required Guid BindingId { get; init; }

    public required string RepositoryNodeId { get; init; }
}
