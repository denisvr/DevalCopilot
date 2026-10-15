namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchPublicationApproval;

/// <summary>Closed statuses of a run's GitHub branch publication approval. <see cref="Unavailable"/> never says why, and
/// <see cref="Available"/> means recorded consent only, never readiness or a push.</summary>
public enum GitHubBranchPublicationApprovalStatus
{
    NotRecorded,
    ApprovalNotRecorded,
    Available,
    Unavailable,
}
