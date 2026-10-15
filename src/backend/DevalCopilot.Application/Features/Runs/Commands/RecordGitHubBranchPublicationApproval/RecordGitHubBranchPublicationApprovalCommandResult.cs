using DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordGitHubBranchPublicationApproval;

/// <summary>The recorded consent: the exact source, target and binding it refers to and the immutable approval. A replay of the same coherent
/// approval returns the original identifiers and time unchanged and implies no execution and no current remote verification.</summary>
public sealed record RecordGitHubBranchPublicationApprovalCommandResult(
    Guid RunId,
    GitHubObservationSourceView Source,
    GitHubObservationTargetView Target,
    GitHubRepositoryIdentityBindingView Binding,
    GitHubBranchPublicationApprovalView Approval);
