using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// The Git side of the explicit local commit (ADR-0029). <see cref="ILocalCommitPreparer"/> builds an immutable commit from
/// physically admitted bytes in an isolated index; <see cref="ILocalCommitRepository"/> moves the one owned branch with an
/// expected-parent <c>update-ref</c>, synchronizes only the worktree's administrative index under an exclusively created owned
/// lock, and proves every outcome from recorded object, branch, index and lock facts. Every invocation goes through
/// <see cref="LocalCommitGitRunner"/>; there is no <c>git add</c>, <c>git commit</c>, <c>reset</c> or <c>checkout</c>, and no
/// working file is ever written.
/// </summary>
public sealed partial class LocalCommitGit(
    IProcessExecutionAdapter processExecutionAdapter,
    IGitWorktreeAdapter worktreeAdapter,
    IWorkspaceOwnershipMarkerStore markerStore,
    LocalCommitStorage storage) : ILocalCommitPreparer, ILocalCommitRepository
{
    private const string ZeroObjectId = "0000000000000000000000000000000000000000";
    private const int PathChunkSize = 24;

    private readonly LocalCommitGitRunner runner = new(processExecutionAdapter, storage);

    // Handles are deliberately process-local and are never reconstructed from a durable receipt. A restart loses this map and
    // therefore loses ownership of any extant lock/quarantine, which recovery treats as attention rather than takeover.
    private readonly ConcurrentDictionary<Guid, WindowsIndexEffectHandles> heldIndexEffects = new();

    /// <summary>Phase hook of the prepared reference transaction (<c>before_start</c>, <c>prepared</c>, <c>proof_passed</c>,
    /// <c>commit_sent</c>, <c>aborted</c> and so on). It is a deterministic observation seam for tests that must place an external
    /// change at an exact boundary; production never sets it and it carries no authority.</summary>
    internal Action<string>? RefTransactionObserver { get; set; }

    /// <summary>The transaction's independent deadline, phase and cleanup budgets (30, 5 and 5 seconds in production).</summary>
    internal LocalCommitRefTransactionBudgets RefTransactionBudgets { get; set; } = LocalCommitRefTransactionBudgets.Production;

    private static string? ResolveGit()
    {
        var descriptor = HostCapabilityCatalog.Get(Capability.Git);
        return HostExecutableResolver.TryResolve(descriptor.CandidateExecutableNames, descriptor.FallbackDirectories);
    }

    private sealed record GitValue(bool Ok, string? Value);

    /// <summary>A single-line Git answer: Ok false when Git could not answer at all, Value null when it answered "absent".</summary>
    private async Task<GitValue> ReadValueAsync(
        string gitPath,
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        string? provenGitDirectory = null)
    {
        var result = await runner.RunAsync(
            gitPath, workspacePath, arguments, cancellationToken, provenGitDirectory: provenGitDirectory);
        if (result.Outcome != LocalCommitGitOutcome.Exited || result.Truncated)
        {
            return new GitValue(false, null);
        }

        var value = result.Output.Trim();
        return result.ExitCode == 0 && value.Length > 0 ? new GitValue(true, value) : new GitValue(result.ExitCode == 1, null);
    }

    /// <summary>Whether HEAD (resolved through any redirect) names the owned branch. Pass the physically proven administrative
    /// directory so the read never rediscovers a repository through the workspace's own <c>.git</c> file.</summary>
    private async Task<bool?> HeadBoundToBranchAsync(
        string gitPath,
        string workspacePath,
        string branchName,
        CancellationToken cancellationToken,
        string? provenGitDirectory = null)
    {
        var head = await ReadValueAsync(
            gitPath, workspacePath, ["symbolic-ref", "-q", "HEAD"], cancellationToken, provenGitDirectory);
        return head.Ok ? string.Equals(head.Value, "refs/heads/" + branchName, StringComparison.Ordinal) : null;
    }

    private async Task<GitValue> ReadBranchTipAsync(
        string gitPath,
        string workspacePath,
        string branchName,
        CancellationToken cancellationToken,
        string? provenGitDirectory = null) =>
        await ReadValueAsync(
            gitPath, workspacePath, ["rev-parse", "--verify", "-q", "refs/heads/" + branchName], cancellationToken, provenGitDirectory);

    private sealed record Ownership(bool Proven, string? AdministrativeDirectory, string? CommonDirectory);

    /// <summary>The worktree is registered with the main repository, its administrative directory cross-validates, and its marker
    /// matches every recorded ownership field (ADR-0008).</summary>
    private async Task<Ownership> ProveOwnershipAsync(
        string mainRepositoryPath, string workspacePath, LocalCommitOwnership ownership, CancellationToken cancellationToken)
    {
        var registration = await worktreeAdapter.IsRegisteredAsync(mainRepositoryPath, workspacePath, cancellationToken);
        var administrative = await worktreeAdapter.ResolveAdministrativeDirectoryAsync(
            mainRepositoryPath, workspacePath, cancellationToken);
        if (registration.Outcome != GitWorktreeRegistrationOutcome.Registered
            || administrative.Outcome != GitWorktreeAdministrativeDirectoryOutcome.Resolved
            || administrative.AdministrativeDirectory is not { } administrativeDirectory)
        {
            return new Ownership(false, null, null);
        }

        var marker = await markerStore.ReadAsync(administrativeDirectory, cancellationToken);
        var matches = marker.Outcome == WorkspaceOwnershipMarkerReadOutcome.Valid
            && marker.Marker is { } found
            && found.WorkspaceId == ownership.WorkspaceId
            && found.ProjectId == ownership.ProjectId
            && found.LeaseId == ownership.LeaseId
            && found.PhysicalVolumeSerialNumber == ownership.PhysicalVolumeSerialNumber
            && string.Equals(found.PhysicalFileIdHex, ownership.PhysicalFileIdHex, StringComparison.OrdinalIgnoreCase);
        return new Ownership(
            matches, matches ? administrativeDirectory : null, matches ? administrative.CommonDirectory : null);
    }

    private static string? IndexSha256(string path) => FileSha256(path);

    /// <summary>SHA-256 of a file read with a shared handle; null when it is absent or unreadable.</summary>
    private static string? FileSha256(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>The exact bytes of the commit object the host asked Git to create, rebuilt from recorded facts alone.</summary>
    internal static string BuildCommitObjectContent(
        string treeSha, string parentSha, string authorName, string authorEmail, long unixSeconds, string commitMessage)
    {
        var identity = $"{authorName} <{authorEmail}> {unixSeconds} +0000";
        return $"tree {treeSha}\nparent {parentSha}\nauthor {identity}\ncommitter {identity}\n\n{commitMessage}";
    }

    /// <summary>Git's SHA-1 object id of a commit with that content, computed here so a stored object can be proven exact without
    /// reading its text back through a redacting pipe.</summary>
    internal static string CommitObjectId(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
#pragma warning disable CA5350 // Git's SHA-1 object identity; used to match it, never for security.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
#pragma warning restore CA5350
        hash.AppendData(Encoding.ASCII.GetBytes($"commit {bytes.Length}\0"));
        hash.AppendData(bytes);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string CommitIdFromFacts(LocalCommitFacts facts) => CommitObjectId(BuildCommitObjectContent(
        facts.TreeSha, facts.ParentCommitSha, facts.AuthorName, facts.AuthorEmail, facts.CommitTimeUnixSeconds, facts.CommitMessage));

    private async Task<LocalCommitObjectState> CommitObjectStateAsync(
        string gitPath,
        string workspacePath,
        LocalCommitFacts facts,
        CancellationToken cancellationToken,
        string? provenGitDirectory = null)
    {
        if (!string.Equals(CommitIdFromFacts(facts), facts.CommitSha, StringComparison.Ordinal))
        {
            return LocalCommitObjectState.Mismatch;
        }

        var exists = await runner.RunAsync(
            gitPath, workspacePath, ["cat-file", "-e", facts.CommitSha], cancellationToken, provenGitDirectory: provenGitDirectory);
        if (exists.Outcome != LocalCommitGitOutcome.Exited || exists.ExitCode > 1)
        {
            return LocalCommitObjectState.Mismatch;
        }

        if (exists.ExitCode == 1)
        {
            return LocalCommitObjectState.Absent;
        }

        var commitType = await ReadValueAsync(
            gitPath, workspacePath, ["cat-file", "-t", facts.CommitSha], cancellationToken, provenGitDirectory);
        var treeType = await ReadValueAsync(
            gitPath, workspacePath, ["cat-file", "-t", facts.TreeSha], cancellationToken, provenGitDirectory);
        return commitType.Value == "commit" && treeType.Value == "tree"
            ? LocalCommitObjectState.ExactMatch
            : LocalCommitObjectState.Mismatch;
    }

    private static IEnumerable<IReadOnlyList<string>> Chunks(IReadOnlyList<string> paths) =>
        paths.Chunk(PathChunkSize).Select(chunk => (IReadOnlyList<string>)chunk);
}
