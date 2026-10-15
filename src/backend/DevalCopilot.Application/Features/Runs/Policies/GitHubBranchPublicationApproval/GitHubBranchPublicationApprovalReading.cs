namespace DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;

/// <summary>What the persisted rows say about a run's publication approval: nothing at all, one coherent version-1 record validated together
/// with its source, target, binding and single Human event, or something that must never be treated as a record.</summary>
internal sealed record GitHubBranchPublicationApprovalReading(
    GitHubBranchPublicationApprovalReadingKind Kind, GitHubBranchPublicationApprovalView? Approval)
{
    public static readonly GitHubBranchPublicationApprovalReading None = new(GitHubBranchPublicationApprovalReadingKind.None, null);

    public static readonly GitHubBranchPublicationApprovalReading Incoherent = new(GitHubBranchPublicationApprovalReadingKind.Incoherent, null);
}
