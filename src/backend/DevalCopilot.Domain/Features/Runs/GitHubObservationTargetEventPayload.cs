using System.Text.Json;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one bounded, canonical version-1 payload of the Human <see cref="RunEventType.GitHubObservationTargetRecorded"/> event
/// (ADR-0040). It is derived only from the target, so the writer and every reader compare the same exact text; it records the
/// selected scope and never a path, credential, remote observation or authority.
/// </summary>
public static class GitHubObservationTargetEventPayload
{
    public const int Version = 1;

    public const int MaximumLength = 1024;

    public static string Build(GitHubObservationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return JsonSerializer.Serialize(new
        {
            version = Version,
            targetId = target.Id,
            operationId = target.LocalCommitOperationId,
            commitSha = target.CommitSha,
            branchName = target.BranchName,
            host = GitHubObservationTarget.Host,
            owner = target.Owner,
            repository = target.Repository,
        });
    }
}
