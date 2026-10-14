using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The Human's explicit, immutable confirmation of the opaque GitHub repository node identifier that a fresh host observation reported for an
/// existing ADR-0040 observation target (ADR-0043). It is historical identity evidence only: never permission, branch or commit proof, CI
/// approval, publication authority or a project-wide alias, and it promises nothing about the remote's future state. At most one exists per
/// target, per run and per local-commit operation (and per event); a node identifier is deliberately not unique across bindings. It is retained
/// forever with the run's history: there is no update, replacement, deletion, backfill or cleanup, and its foreign keys are restrictive.
/// The host and purpose are fixed project contract, never client choices.
/// </summary>
public sealed class GitHubRepositoryIdentityBinding
{
    private GitHubRepositoryIdentityBinding()
    {
    }

    public static GitHubRepositoryIdentityBinding Record(
        Guid id,
        Guid runId,
        Guid projectId,
        Guid localCommitOperationId,
        Guid gitWorkspaceId,
        Guid observationTargetId,
        string commitSha,
        string branchName,
        string repositoryNodeId,
        string owner,
        string repository,
        DateTimeOffset targetRecordedAtUtc,
        DateTimeOffset observedAtUtc,
        DateTimeOffset recordedAtUtc,
        Guid eventId)
    {
        RequireIdentifier(id, nameof(id));
        RequireIdentifier(runId, nameof(runId));
        RequireIdentifier(projectId, nameof(projectId));
        RequireIdentifier(localCommitOperationId, nameof(localCommitOperationId));
        RequireIdentifier(gitWorkspaceId, nameof(gitWorkspaceId));
        RequireIdentifier(observationTargetId, nameof(observationTargetId));
        RequireIdentifier(eventId, nameof(eventId));
        if (!GitHubObservationTarget.IsCommitSha(commitSha))
        {
            throw new ArgumentException("The commit must be exactly 40 lowercase hexadecimal characters.", nameof(commitSha));
        }

        if (string.IsNullOrWhiteSpace(branchName) || branchName.Length > GitHubObservationTarget.MaximumBranchNameLength)
        {
            throw new ArgumentException("The branch must be present and bounded.", nameof(branchName));
        }

        if (!GitHubRepositoryNodeIdPolicy.IsNodeId(repositoryNodeId))
        {
            throw new ArgumentException("The repository node identifier is outside the supported subset.", nameof(repositoryNodeId));
        }

        if (!GitHubRepositoryNamePolicy.IsOwner(owner))
        {
            throw new ArgumentException("The owner is outside the supported subset.", nameof(owner));
        }

        if (!GitHubRepositoryNamePolicy.IsRepository(repository))
        {
            throw new ArgumentException("The repository is outside the supported subset.", nameof(repository));
        }

        if (targetRecordedAtUtc.Offset != TimeSpan.Zero || observedAtUtc.Offset != TimeSpan.Zero || recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Every time must be UTC.", nameof(recordedAtUtc));
        }

        if (observedAtUtc < targetRecordedAtUtc || recordedAtUtc < observedAtUtc)
        {
            throw new ArgumentException("The target must be recorded before it is observed, and observed before it is bound.", nameof(observedAtUtc));
        }

        return new GitHubRepositoryIdentityBinding
        {
            Id = id,
            RunId = runId,
            ProjectId = projectId,
            LocalCommitOperationId = localCommitOperationId,
            GitWorkspaceId = gitWorkspaceId,
            ObservationTargetId = observationTargetId,
            CommitSha = commitSha,
            BranchName = branchName,
            RepositoryNodeId = repositoryNodeId,
            Owner = owner,
            Repository = repository,
            ObservedAtUtc = observedAtUtc,
            RecordedAtUtc = recordedAtUtc,
            EventId = eventId,
        };
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid LocalCommitOperationId { get; private set; }

    public Guid GitWorkspaceId { get; private set; }

    public Guid ObservationTargetId { get; private set; }

    public string CommitSha { get; private set; } = string.Empty;

    public string BranchName { get; private set; } = string.Empty;

    public string RepositoryNodeId { get; private set; } = string.Empty;

    public string Owner { get; private set; } = string.Empty;

    public string Repository { get; private set; } = string.Empty;

    public DateTimeOffset ObservedAtUtc { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>The identifier of the one Human <see cref="RunEventType.GitHubRepositoryIdentityBound"/> event saved with it.</summary>
    public Guid EventId { get; private set; }

    private static void RequireIdentifier(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", name);
        }
    }
}
