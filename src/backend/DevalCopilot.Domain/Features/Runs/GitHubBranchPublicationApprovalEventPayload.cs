using System.Text.Json;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one bounded, canonical version-1 payload of the Human <see cref="RunEventType.GitHubBranchPublicationApproved"/> event (ADR-0048).
/// It is derived only from the approval, so the writer and every reader compare the same exact text; it is the complete approval snapshot
/// and its time, and it records consent only: never a path, credential, URL, refspec, message, permission or an executed publication. The
/// 4 KiB bound covers the longest supported node identifier, both name pairs, branch and times.
/// </summary>
public static class GitHubBranchPublicationApprovalEventPayload
{
    public const int Version = 1;

    public const int MaximumLength = 4096;

    public static string Build(GitHubBranchPublicationApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);

        return JsonSerializer.Serialize(new
        {
            version = Version,
            approvalId = approval.Id,
            runId = approval.RunId,
            projectId = approval.ProjectId,
            operationId = approval.LocalCommitOperationId,
            workspaceId = approval.GitWorkspaceId,
            commitSha = approval.CommitSha,
            branchName = approval.BranchName,
            host = GitHubBranchPublicationApproval.Host,
            action = GitHubBranchPublicationApproval.Action,
            policyVersion = GitHubBranchPublicationApproval.PolicyVersion,
            target = new
            {
                id = approval.TargetId,
                eventId = approval.TargetEventId,
                owner = approval.TargetOwner,
                repository = approval.TargetRepository,
                recordedAtUtc = approval.TargetRecordedAtUtc,
            },
            binding = new
            {
                id = approval.BindingId,
                eventId = approval.BindingEventId,
                repositoryNodeId = approval.RepositoryNodeId,
                owner = approval.BindingOwner,
                repository = approval.BindingRepository,
                observedAtUtc = approval.BindingObservedAtUtc,
                recordedAtUtc = approval.BindingRecordedAtUtc,
            },
            approvedAtUtc = approval.ApprovedAtUtc,
        });
    }
}
