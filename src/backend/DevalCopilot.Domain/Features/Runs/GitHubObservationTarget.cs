using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The Human's explicit, immutable selection of one GitHub repository scope for the exact branch and commit of a completed manual
/// local delivery (ADR-0040). It is local scope selection only: never verified remote identity, permission, CI success,
/// publication approval or automatic observation authority, and nothing here implies any of them. At most one exists per run
/// and per local-commit operation. It is retained forever with the run's history: there is no update, replacement, deletion,
/// backfill or cleanup, and its foreign keys are restrictive so nothing can silently remove it. The host and purpose are fixed
/// project contract, never client choices.
/// </summary>
public sealed class GitHubObservationTarget
{
    public const string Host = "github.com";

    public const string Purpose = "CiObservation";

    public const int MaximumBranchNameLength = 512;

    private GitHubObservationTarget()
    {
    }

    public static GitHubObservationTarget Record(
        Guid id,
        Guid runId,
        Guid projectId,
        Guid localCommitOperationId,
        Guid gitWorkspaceId,
        string commitSha,
        string branchName,
        string owner,
        string repository,
        DateTimeOffset recordedAtUtc,
        Guid eventId)
    {
        RequireIdentifier(id, nameof(id));
        RequireIdentifier(runId, nameof(runId));
        RequireIdentifier(projectId, nameof(projectId));
        RequireIdentifier(localCommitOperationId, nameof(localCommitOperationId));
        RequireIdentifier(gitWorkspaceId, nameof(gitWorkspaceId));
        RequireIdentifier(eventId, nameof(eventId));
        if (!IsCommitSha(commitSha))
        {
            throw new ArgumentException("The commit must be exactly 40 lowercase hexadecimal characters.", nameof(commitSha));
        }

        if (string.IsNullOrWhiteSpace(branchName) || branchName.Length > MaximumBranchNameLength)
        {
            throw new ArgumentException("The branch must be present and bounded.", nameof(branchName));
        }

        if (!GitHubRepositoryNamePolicy.IsOwner(owner))
        {
            throw new ArgumentException("The owner is outside the supported subset.", nameof(owner));
        }

        if (!GitHubRepositoryNamePolicy.IsRepository(repository))
        {
            throw new ArgumentException("The repository is outside the supported subset.", nameof(repository));
        }

        if (recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The recorded time must be UTC.", nameof(recordedAtUtc));
        }

        return new GitHubObservationTarget
        {
            Id = id,
            RunId = runId,
            ProjectId = projectId,
            LocalCommitOperationId = localCommitOperationId,
            GitWorkspaceId = gitWorkspaceId,
            CommitSha = commitSha,
            BranchName = branchName,
            Owner = owner,
            Repository = repository,
            RecordedAtUtc = recordedAtUtc,
            EventId = eventId,
        };
    }

    /// <summary>Exactly 40 lowercase hexadecimal characters, the only commit form this contract admits.</summary>
    public static bool IsCommitSha(string? value) =>
        value is { Length: 40 } && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid LocalCommitOperationId { get; private set; }

    public Guid GitWorkspaceId { get; private set; }

    public string CommitSha { get; private set; } = string.Empty;

    public string BranchName { get; private set; } = string.Empty;

    public string Owner { get; private set; } = string.Empty;

    public string Repository { get; private set; } = string.Empty;

    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>The identifier of the one Human <see cref="RunEventType.GitHubObservationTargetRecorded"/> event saved with it.</summary>
    public Guid EventId { get; private set; }

    private static void RequireIdentifier(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", name);
        }
    }
}
