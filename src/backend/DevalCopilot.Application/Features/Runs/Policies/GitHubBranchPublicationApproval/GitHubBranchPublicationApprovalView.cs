namespace DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;

/// <summary>The immutable Human approval of ONE future normal push (ADR-0048): the identifiers it consented to, the fixed host, action and
/// policy version, the exact recorded commit and branch, the repository named by the target (its owner and repository copy the target
/// names) and the UTC approval time. It is recorded consent only: never readiness, eligibility, permission, a pushed commit or any other
/// publication, and nothing here proves the remote's current state.</summary>
public sealed record GitHubBranchPublicationApprovalView(
    Guid Id,
    Guid EventId,
    string Host,
    string Action,
    int PolicyVersion,
    Guid LocalCommitOperationId,
    Guid WorkspaceId,
    Guid TargetId,
    Guid BindingId,
    string RepositoryNodeId,
    string Owner,
    string Repository,
    string BranchName,
    string CommitSha,
    DateTimeOffset ApprovedAtUtc);
