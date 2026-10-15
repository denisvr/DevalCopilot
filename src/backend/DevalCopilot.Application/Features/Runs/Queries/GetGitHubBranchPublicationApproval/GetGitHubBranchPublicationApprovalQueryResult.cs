using DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchPublicationApproval;

/// <summary><see cref="GitHubBranchPublicationApprovalStatus.NotRecorded"/> and <see cref="GitHubBranchPublicationApprovalStatus.Unavailable"/>
/// carry no fact; <see cref="GitHubBranchPublicationApprovalStatus.ApprovalNotRecorded"/> carries the coherent source, target and binding
/// and no approval; <see cref="GitHubBranchPublicationApprovalStatus.Available"/> carries all four, validated together.</summary>
public sealed record GetGitHubBranchPublicationApprovalQueryResult(
    Guid RunId,
    GitHubBranchPublicationApprovalStatus Status,
    GitHubObservationSourceView? Source,
    GitHubObservationTargetView? Target,
    GitHubRepositoryIdentityBindingView? Binding,
    GitHubBranchPublicationApprovalView? Approval);
