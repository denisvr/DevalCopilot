using DevalCopilot.Api.Features.Runs.GetGitHubBranchPublicationApproval;

namespace DevalCopilot.Api.Features.Runs.RecordGitHubBranchPublicationApproval;

/// <summary>The recorded consent: the exact source, target and binding it refers to and the immutable approval. A replay of the same
/// coherent approval returns the original record unchanged and implies no execution and no current remote verification.</summary>
public sealed record RecordGitHubBranchPublicationApprovalResponse(
    Guid RunId,
    GitHubBranchPublicationApprovalSourceResponse Source,
    GitHubBranchPublicationApprovalTargetResponse Target,
    GitHubBranchPublicationApprovalBindingResponse Binding,
    GitHubBranchPublicationApprovalResponse Approval);
