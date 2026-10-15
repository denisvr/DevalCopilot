namespace DevalCopilot.Api.Features.Runs.GetGitHubBranchPublicationApproval;

/// <summary>The pinned source the approval refers to (ADR-0048): the completed local-commit operation, its exact lowercase commit and its
/// exact recorded owned branch. It is recorded evidence, never current repository state.</summary>
public sealed record GitHubBranchPublicationApprovalSourceResponse(Guid OperationId, string CommitSha, string BranchName);
