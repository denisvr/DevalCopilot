using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// Creates and inspects tool-owned Git worktrees, composed entirely on top of
/// <see cref="IProcessExecutionAdapter"/> — never a second child-process path. Reuses the exact
/// same fixed hardening discipline as <see cref="GitRepositoryInspector"/> (deliberately
/// duplicated rather than shared, to avoid destabilizing that already-verified type): every
/// invocation is fixed, bounded, argument-list-only, with <c>core.fsmonitor</c> and
/// <c>core.untrackedCache</c> forced off and remote protocols disabled regardless of the
/// target repository's own config. Exactly one command ever mutates anything
/// (<see cref="CreateAsync"/>'s <c>worktree add</c>), and it only ever creates a brand-new
/// branch and a brand-new linked worktree — it never touches the main checkout's <c>HEAD</c>,
/// index, or any pre-existing ref. See ADR-0008.
/// </summary>
public sealed class GitWorktreeAdapter(IProcessExecutionAdapter processExecutionAdapter) : IGitWorktreeAdapter
{
    /// <summary>Generous but finite: unlike the read-only probes below, worktree creation does
    /// real file I/O (a checkout), so it is bounded more loosely than
    /// <see cref="ReadTimeout"/>.</summary>
    private static readonly TimeSpan CreationTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private const int MaxCapturedBytes = 4 * 1024;

    private static readonly IReadOnlyList<string> HardeningPrefix =
        ["--no-pager", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false", "-c", "protocol.allow=never"];

    private static readonly IReadOnlyDictionary<string, string> HardeningEnvironment =
        new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_OPTIONAL_LOCKS"] = "0" };

    public async Task<GitWorktreeCreationResult> CreateAsync(
        string mainRepositoryPath, string workspacePath, string branchName, string resolvedCommitSha, CancellationToken cancellationToken)
    {
        // The most fundamental structural safety check, performed first and requiring neither
        // git.exe nor any filesystem access: the computed workspace path must be neither equal
        // to, nested inside, nor an ancestor of the main repository path. Checked before any
        // Git invocation or filesystem mutation is even attempted.
        if (PathsOverlap(workspacePath, mainRepositoryPath))
        {
            return new GitWorktreeCreationResult(GitWorktreeCreationOutcome.WorkspaceOverlapsMainRepository);
        }

        var resolvedGitPath = ResolveGit();
        if (resolvedGitPath is null)
        {
            return new GitWorktreeCreationResult(GitWorktreeCreationOutcome.GitUnavailable);
        }

        // The path-collision safety check the Application layer must never perform itself:
        // found unexpectedly occupied, this fails closed rather than deleting or overwriting
        // anything already there.
        if (Directory.Exists(workspacePath) || File.Exists(workspacePath))
        {
            return new GitWorktreeCreationResult(GitWorktreeCreationOutcome.PathAlreadyExists);
        }

        var result = await RunAsync(
            resolvedGitPath,
            mainRepositoryPath,
            ["worktree", "add", "-b", branchName, workspacePath, resolvedCommitSha],
            CreationTimeout,
            cancellationToken);

        if (result.Outcome == GitCommandOutcome.TimedOut)
        {
            return new GitWorktreeCreationResult(GitWorktreeCreationOutcome.GitInvocationTimedOut);
        }

        return Succeeded(result)
            ? new GitWorktreeCreationResult(GitWorktreeCreationOutcome.Success)
            : new GitWorktreeCreationResult(GitWorktreeCreationOutcome.GitInvocationFailed);
    }

    public async Task<GitWorktreeAdministrativeDirectoryResult> ResolveAdministrativeDirectoryAsync(
        string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken)
    {
        var resolvedGitPath = ResolveGit();
        if (resolvedGitPath is null)
        {
            return new GitWorktreeAdministrativeDirectoryResult(GitWorktreeAdministrativeDirectoryOutcome.GitInvocationFailed, null, null);
        }

        var workspaceGitDir = await RunAsync(resolvedGitPath, workspacePath, ["rev-parse", "--git-dir"], ReadTimeout, cancellationToken);
        if (workspaceGitDir.Outcome == GitCommandOutcome.TimedOut)
        {
            return new GitWorktreeAdministrativeDirectoryResult(GitWorktreeAdministrativeDirectoryOutcome.GitInvocationTimedOut, null, null);
        }

        if (!Succeeded(workspaceGitDir))
        {
            return new GitWorktreeAdministrativeDirectoryResult(GitWorktreeAdministrativeDirectoryOutcome.NotAWorktree, null, null);
        }

        var workspaceCommonDir = await RunAsync(resolvedGitPath, workspacePath, ["rev-parse", "--git-common-dir"], ReadTimeout, cancellationToken);
        var mainCommonDir = await RunAsync(resolvedGitPath, mainRepositoryPath, ["rev-parse", "--git-common-dir"], ReadTimeout, cancellationToken);

        if (workspaceCommonDir.Outcome == GitCommandOutcome.TimedOut || mainCommonDir.Outcome == GitCommandOutcome.TimedOut)
        {
            return new GitWorktreeAdministrativeDirectoryResult(GitWorktreeAdministrativeDirectoryOutcome.GitInvocationTimedOut, null, null);
        }

        if (!Succeeded(workspaceCommonDir) || !Succeeded(mainCommonDir))
        {
            return new GitWorktreeAdministrativeDirectoryResult(GitWorktreeAdministrativeDirectoryOutcome.NotAWorktree, null, null);
        }

        var resolvedWorkspaceCommonDir = ResolveAgainst(workspaceCommonDir.Output, workspacePath);
        var resolvedMainCommonDir = ResolveAgainst(mainCommonDir.Output, mainRepositoryPath);

        if (!PathsMatch(resolvedWorkspaceCommonDir, resolvedMainCommonDir))
        {
            // Git answered, but this workspace's own common directory does not point back at
            // the expected main repository — never assumed to be "our" worktree by path shape
            // alone.
            return new GitWorktreeAdministrativeDirectoryResult(GitWorktreeAdministrativeDirectoryOutcome.NotAWorktree, null, null);
        }

        var resolvedWorkspaceGitDir = ResolveAgainst(workspaceGitDir.Output, workspacePath);
        return new GitWorktreeAdministrativeDirectoryResult(
            GitWorktreeAdministrativeDirectoryOutcome.Resolved, resolvedWorkspaceGitDir, resolvedWorkspaceCommonDir);
    }

    public async Task<GitWorktreeHeadResult> GetHeadCommitShaAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var resolvedGitPath = ResolveGit();
        if (resolvedGitPath is null)
        {
            return new GitWorktreeHeadResult(GitWorktreeHeadOutcome.GitInvocationFailed, null);
        }

        var result = await RunAsync(resolvedGitPath, workspacePath, ["rev-parse", "HEAD"], ReadTimeout, cancellationToken);
        if (result.Outcome == GitCommandOutcome.TimedOut)
        {
            return new GitWorktreeHeadResult(GitWorktreeHeadOutcome.GitInvocationTimedOut, null);
        }

        return Succeeded(result)
            ? new GitWorktreeHeadResult(GitWorktreeHeadOutcome.Resolved, result.Output)
            : new GitWorktreeHeadResult(GitWorktreeHeadOutcome.GitInvocationFailed, null);
    }

    public async Task<GitWorktreeRegistrationResult> IsRegisteredAsync(
        string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken)
    {
        var resolvedGitPath = ResolveGit();
        if (resolvedGitPath is null)
        {
            return new GitWorktreeRegistrationResult(GitWorktreeRegistrationOutcome.GitInvocationFailed);
        }

        var result = await RunAsync(resolvedGitPath, mainRepositoryPath, ["worktree", "list", "--porcelain"], ReadTimeout, cancellationToken);
        if (result.Outcome == GitCommandOutcome.TimedOut)
        {
            return new GitWorktreeRegistrationResult(GitWorktreeRegistrationOutcome.GitInvocationTimedOut);
        }

        if (!Succeeded(result))
        {
            return new GitWorktreeRegistrationResult(GitWorktreeRegistrationOutcome.GitInvocationFailed);
        }

        var canonicalWorkspacePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspacePath));
        foreach (var rawLine in result.Output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                continue;
            }

            var listedPath = line["worktree ".Length..].Trim().Replace('/', '\\');
            var canonicalListedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(listedPath));
            if (string.Equals(canonicalListedPath, canonicalWorkspacePath, StringComparison.OrdinalIgnoreCase))
            {
                return new GitWorktreeRegistrationResult(GitWorktreeRegistrationOutcome.Registered);
            }
        }

        return new GitWorktreeRegistrationResult(GitWorktreeRegistrationOutcome.NotRegistered);
    }

    private static string? ResolveGit()
    {
        var descriptor = HostCapabilityCatalog.Get(Capability.Git);
        return HostExecutableResolver.TryResolve(descriptor.CandidateExecutableNames, descriptor.FallbackDirectories);
    }

    private enum GitCommandOutcome
    {
        Exited,
        TimedOut,
        LaunchFailed,
    }

    private readonly record struct GitCommandResult(GitCommandOutcome Outcome, int ExitCode, string Output);

    private static bool Succeeded(GitCommandResult result) => result.Outcome == GitCommandOutcome.Exited && result.ExitCode == 0;

    private async Task<GitCommandResult> RunAsync(
        string resolvedGitPath, string workingDirectory, IReadOnlyList<string> subcommandArguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var arguments = new List<string>(HardeningPrefix.Count + subcommandArguments.Count);
        arguments.AddRange(HardeningPrefix);
        arguments.AddRange(subcommandArguments);

        var request = new ProcessExecutionRequest
        {
            ExecutablePath = resolvedGitPath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            ApprovedRoot = workingDirectory,
            Timeout = timeout,
            MaxBytesPerStream = MaxCapturedBytes,
            MaxTotalCapturedBytes = MaxCapturedBytes,
            EnvironmentVariables = HardeningEnvironment,
        };

        ProcessExecutionResult result;
        try
        {
            result = await processExecutionAdapter.ExecuteAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new GitCommandResult(GitCommandOutcome.LaunchFailed, 0, string.Empty);
        }

        switch (result.Outcome)
        {
            case ProcessExecutionOutcome.Cancelled:
                throw new OperationCanceledException(cancellationToken);
            case ProcessExecutionOutcome.TimedOut:
                return new GitCommandResult(GitCommandOutcome.TimedOut, 0, string.Empty);
        }

        return new GitCommandResult(GitCommandOutcome.Exited, result.ExitCode!.Value, result.StandardOutput.Trim());
    }

    /// <summary>Git prints paths with forward slashes even on Windows; both sides are
    /// normalized before comparison.</summary>
    private static bool PathsMatch(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    /// <summary><c>--git-dir</c>/<c>--git-common-dir</c> print a path relative to the working
    /// directory for the common case and an absolute path for a linked worktree — resolved to a
    /// common absolute, slash-normalized form before comparison either way.</summary>
    private static string ResolveAgainst(string gitReportedPath, string workingDirectory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(gitReportedPath.Replace('/', '\\'), workingDirectory));

    /// <summary>
    /// True when <paramref name="pathA"/> and <paramref name="pathB"/> are equal, or one is an
    /// ancestor directory of the other — a path-segment-aware comparison, never a raw string
    /// prefix check (which would, for example, incorrectly treat <c>C:\repos\Foo</c> as
    /// contained in <c>C:\repos\Fo</c>). Both inputs are canonicalized (full path, no trailing
    /// separator) before comparison; segments are compared case-insensitively, matching Windows
    /// path semantics.
    /// </summary>
    private static bool PathsOverlap(string pathA, string pathB)
    {
        var segmentsA = CanonicalSegments(pathA);
        var segmentsB = CanonicalSegments(pathB);

        var (shorter, longer) = segmentsA.Length <= segmentsB.Length ? (segmentsA, segmentsB) : (segmentsB, segmentsA);

        for (var index = 0; index < shorter.Length; index++)
        {
            if (!string.Equals(shorter[index], longer[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // The shorter segment list is a prefix of the longer one (or they are the same length,
        // i.e. equal paths) — either direction of containment, or equality.
        return true;
    }

    private static string[] CanonicalSegments(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
}
