namespace DevalCopilot.Api.Features.Runs.GetGitHubBranchPublicationApproval;

/// <summary>One of NotRecorded, ApprovalNotRecorded, Available or Unavailable. <c>Source</c>, <c>Target</c> and <c>Binding</c> are present
/// for ApprovalNotRecorded and Available; <c>Approval</c> only for Available. NotRecorded and Unavailable carry no fact and Unavailable
/// never says why. A recorded approval is historical consent, never readiness, a permission or a push.</summary>
public sealed record GetGitHubBranchPublicationApprovalResponse(
    Guid RunId,
    string Status,
    GitHubBranchPublicationApprovalSourceResponse? Source,
    GitHubBranchPublicationApprovalTargetResponse? Target,
    GitHubBranchPublicationApprovalBindingResponse? Binding,
    GitHubBranchPublicationApprovalResponse? Approval);
