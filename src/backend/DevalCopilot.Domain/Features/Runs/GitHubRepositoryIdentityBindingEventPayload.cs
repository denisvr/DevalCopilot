using System.Text.Json;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one bounded, canonical version-1 payload of the Human <see cref="RunEventType.GitHubRepositoryIdentityBound"/> event (ADR-0043). It
/// is derived only from the binding, so the writer and every reader compare the same exact text; it records the confirmed identity and
/// never a path, credential, permission or authority. The 4 KiB bound covers the longest supported node identifier, names, branch and time.
/// </summary>
public static class GitHubRepositoryIdentityBindingEventPayload
{
    public const int Version = 1;

    public const int MaximumLength = 4096;

    public static string Build(GitHubRepositoryIdentityBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return JsonSerializer.Serialize(new
        {
            version = Version,
            bindingId = binding.Id,
            targetId = binding.ObservationTargetId,
            operationId = binding.LocalCommitOperationId,
            commitSha = binding.CommitSha,
            branchName = binding.BranchName,
            host = GitHubObservationTarget.Host,
            repositoryNodeId = binding.RepositoryNodeId,
            owner = binding.Owner,
            repository = binding.Repository,
            observedAtUtc = binding.ObservedAtUtc,
        });
    }
}
