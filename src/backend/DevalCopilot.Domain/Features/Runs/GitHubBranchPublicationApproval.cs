using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The Human's explicit, immutable approval of ONE future normal push of the exact completed local commit to its exact recorded owned
/// branch in the repository named by a coherent ADR-0040 target and ADR-0043 binding (ADR-0048). It is recorded consent only: it executes,
/// observes and asserts nothing, it is never readiness, eligibility, permission, a pushed commit, a pull request, a force push, a merge,
/// a deletion, a rerun or automatic execution, and a future executor needs a separate explicit request with fresh ownership, commit,
/// remote identity, reference, authentication, permission and ambiguity checks. It carries complete exact snapshots of the source, the
/// target and the binding it consented to; target names and binding observed names are kept separately. At most one exists per run, per
/// local-commit operation, per target, per binding and per event. It is retained forever with the run's history: there is no update,
/// replacement, revocation, deletion, backfill or cleanup and its foreign keys are restrictive. The host, action and policy version are
/// fixed project contract, never client choices or columns.
/// </summary>
public sealed class GitHubBranchPublicationApproval
{
    public const string Host = GitHubObservationTarget.Host;

    public const string Action = "PushExactCommit";

    public const int PolicyVersion = 1;

    private GitHubBranchPublicationApproval()
    {
    }

    public static GitHubBranchPublicationApproval Record(
        Guid id,
        Guid eventId,
        Guid runId,
        Guid projectId,
        Guid localCommitOperationId,
        Guid gitWorkspaceId,
        string commitSha,
        string branchName,
        Guid targetId,
        Guid targetEventId,
        string targetOwner,
        string targetRepository,
        DateTimeOffset targetRecordedAtUtc,
        Guid bindingId,
        Guid bindingEventId,
        string repositoryNodeId,
        string bindingOwner,
        string bindingRepository,
        DateTimeOffset bindingObservedAtUtc,
        DateTimeOffset bindingRecordedAtUtc,
        DateTimeOffset approvedAtUtc)
    {
        RequireIdentifier(id, nameof(id));
        RequireIdentifier(eventId, nameof(eventId));
        RequireIdentifier(runId, nameof(runId));
        RequireIdentifier(projectId, nameof(projectId));
        RequireIdentifier(localCommitOperationId, nameof(localCommitOperationId));
        RequireIdentifier(gitWorkspaceId, nameof(gitWorkspaceId));
        RequireIdentifier(targetId, nameof(targetId));
        RequireIdentifier(targetEventId, nameof(targetEventId));
        RequireIdentifier(bindingId, nameof(bindingId));
        RequireIdentifier(bindingEventId, nameof(bindingEventId));
        if (!GitHubObservationTarget.IsCommitSha(commitSha))
        {
            throw new ArgumentException("The commit must be exactly 40 lowercase hexadecimal characters.", nameof(commitSha));
        }

        // The branch is the workspace branch the host computes, which is printable ASCII; requiring that here also keeps the canonical
        // payload within its bound, because the JSON writer would otherwise expand every other character to a six-byte escape.
        if (string.IsNullOrEmpty(branchName) || branchName.Length > GitHubObservationTarget.MaximumBranchNameLength
            || !branchName.All(character => character is > ' ' and <= '~'))
        {
            throw new ArgumentException("The branch must be present, bounded and printable ASCII without spaces.", nameof(branchName));
        }

        if (!GitHubRepositoryNodeIdPolicy.IsNodeId(repositoryNodeId))
        {
            throw new ArgumentException("The repository node identifier is outside the supported subset.", nameof(repositoryNodeId));
        }

        if (!GitHubRepositoryNamePolicy.IsOwner(targetOwner) || !GitHubRepositoryNamePolicy.IsOwner(bindingOwner))
        {
            throw new ArgumentException("An owner is outside the supported subset.", nameof(targetOwner));
        }

        if (!GitHubRepositoryNamePolicy.IsRepository(targetRepository) || !GitHubRepositoryNamePolicy.IsRepository(bindingRepository))
        {
            throw new ArgumentException("A repository is outside the supported subset.", nameof(targetRepository));
        }

        // The supported names are ASCII, so ordinal-ignore-case is exactly an ASCII case difference.
        if (!string.Equals(targetOwner, bindingOwner, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(targetRepository, bindingRepository, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The binding names must equal the target names ignoring only ASCII case.", nameof(bindingOwner));
        }

        if (targetRecordedAtUtc.Offset != TimeSpan.Zero || bindingObservedAtUtc.Offset != TimeSpan.Zero
            || bindingRecordedAtUtc.Offset != TimeSpan.Zero || approvedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Every time must be UTC.", nameof(approvedAtUtc));
        }

        if (bindingObservedAtUtc < targetRecordedAtUtc || bindingRecordedAtUtc < bindingObservedAtUtc || approvedAtUtc < bindingRecordedAtUtc)
        {
            throw new ArgumentException(
                "The target must be recorded before it is observed, observed before it is bound, and bound before it is approved.",
                nameof(approvedAtUtc));
        }

        return new GitHubBranchPublicationApproval
        {
            Id = id,
            EventId = eventId,
            RunId = runId,
            ProjectId = projectId,
            LocalCommitOperationId = localCommitOperationId,
            GitWorkspaceId = gitWorkspaceId,
            CommitSha = commitSha,
            BranchName = branchName,
            TargetId = targetId,
            TargetEventId = targetEventId,
            TargetOwner = targetOwner,
            TargetRepository = targetRepository,
            TargetRecordedAtUtc = targetRecordedAtUtc,
            BindingId = bindingId,
            BindingEventId = bindingEventId,
            RepositoryNodeId = repositoryNodeId,
            BindingOwner = bindingOwner,
            BindingRepository = bindingRepository,
            BindingObservedAtUtc = bindingObservedAtUtc,
            BindingRecordedAtUtc = bindingRecordedAtUtc,
            ApprovedAtUtc = approvedAtUtc,
        };
    }

    private static void RequireIdentifier(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", name);
        }
    }

    public Guid Id { get; private set; }

    /// <summary>The identifier of the one Human <see cref="RunEventType.GitHubBranchPublicationApproved"/> event saved with it.</summary>
    public Guid EventId { get; private set; }

    public Guid RunId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid LocalCommitOperationId { get; private set; }

    public Guid GitWorkspaceId { get; private set; }

    public string CommitSha { get; private set; } = string.Empty;

    public string BranchName { get; private set; } = string.Empty;

    public Guid TargetId { get; private set; }

    public Guid TargetEventId { get; private set; }

    public string TargetOwner { get; private set; } = string.Empty;

    public string TargetRepository { get; private set; } = string.Empty;

    public DateTimeOffset TargetRecordedAtUtc { get; private set; }

    public Guid BindingId { get; private set; }

    public Guid BindingEventId { get; private set; }

    public string RepositoryNodeId { get; private set; } = string.Empty;

    public string BindingOwner { get; private set; } = string.Empty;

    public string BindingRepository { get; private set; } = string.Empty;

    public DateTimeOffset BindingObservedAtUtc { get; private set; }

    public DateTimeOffset BindingRecordedAtUtc { get; private set; }

    public DateTimeOffset ApprovedAtUtc { get; private set; }
}
