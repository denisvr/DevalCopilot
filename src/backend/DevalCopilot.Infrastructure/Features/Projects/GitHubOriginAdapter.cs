using System.ComponentModel;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// Observes the repository-local GitHub origin of one already filesystem-validated project root with exactly two fixed,
/// read-only Git invocations composed on the existing <see cref="IProcessExecutionAdapter"/> (never a shell or a second
/// process engine), both inside the project root as their own approved root, under one 15 second budget:
/// <list type="number">
/// <item><description><c>rev-parse --is-bare-repository --git-dir --git-common-dir --show-toplevel</c>, the minimal shape
/// check: a non-bare repository whose top level is exactly this root and whose Git directory and common directory are both
/// exactly <c>&lt;root&gt;\.git</c>. A parent-directory repository, a bare repository, a linked worktree, a submodule or
/// separate Git directory and any other shape is refused before any configuration is read.</description></item>
/// <item><description><c>config --file &lt;root&gt;\.git\config --no-includes --null --get-all remote.origin.url</c>: only that one
/// file, the repository's own configuration, is a candidate, and it is bound by path to the held scope rather than found by a
/// second repository discovery. Global, system, included, worktree and command-line values, URL rewrites
/// (<c>insteadOf</c>) and <c>pushurl</c> are never read, and nothing contacts a network, a credential helper or a
/// hook.</description></item>
/// </list>
/// Before the first invocation the root's own <c>.git</c> directory and <c>config</c> file are validated (real, non-reparse,
/// regular) and the file is held open without delete sharing for the whole observation (<see cref="GitHubOriginScopePin"/>),
/// which pins the directory against being moved, replaced by a gitfile or removed; a vanished, replaced or redirected scope
/// is <see cref="GitHubOriginStatus.Unavailable"/>, never a foreign value and never a misleading NotConfigured.
/// The child receives only fixed Git controls (no ambient value is forwarded): prompts and optional locks disabled and the
/// system and global configuration replaced by the null device. Exit 1 with empty untruncated output is
/// <see cref="GitHubOriginStatus.NotConfigured"/>; exit 0 with exactly one non-empty NUL-terminated supported value is
/// <see cref="GitHubOriginStatus.Available"/>; everything else (a duplicate value even when identical, malformed framing,
/// an unsupported value, any other exit, timeout, truncation or an expected launch failure) is
/// <see cref="GitHubOriginStatus.Unavailable"/>. The raw value, configuration and stderr are transient: never returned,
/// logged or stored. Only the caller's own cancellation is raised, and any other fault propagates.
/// </summary>
public sealed class GitHubOriginAdapter : IGitHubOriginObserver
{
    internal static readonly TimeSpan ObservationBudget = TimeSpan.FromSeconds(15);
    internal const int MaxBytesPerStream = 8 * 1024;
    internal const int MaxTotalCapturedBytes = 16 * 1024;

    // The established inspector hardening, applied to every invocation regardless of what the repository's own
    // (untrusted) configuration requests.
    internal static readonly string[] HardeningPrefix =
        ["--no-pager", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false", "-c", "protocol.allow=never"];

    internal static readonly string[] ShapeArguments =
        ["rev-parse", "--is-bare-repository", "--git-dir", "--git-common-dir", "--show-toplevel"];

    /// <summary>The configuration lookup is bound to the one admitted file, never to a second repository discovery.</summary>
    internal static string[] ConfigArguments(string root) =>
        ["config", "--file", GitHubOriginScopePin.ConfigPathOf(root), "--no-includes", "--null", "--get-all", "remote.origin.url"];

    /// <summary>The whole child environment; the host's ambient variables are never forwarded.</summary>
    internal static IReadOnlyDictionary<string, string> ChildEnvironment { get; } = new Dictionary<string, string>
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_OPTIONAL_LOCKS"] = "0",
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null",
    };

    private readonly IProcessExecutionAdapter _processExecutionAdapter;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, IGitHubOriginScope?> _acquireScope;

    public GitHubOriginAdapter(IProcessExecutionAdapter processExecutionAdapter, TimeProvider timeProvider)
        : this(processExecutionAdapter, timeProvider, GitHubOriginScopePin.TryAcquire)
    {
    }

    internal GitHubOriginAdapter(
        IProcessExecutionAdapter processExecutionAdapter, TimeProvider timeProvider, Func<string, IGitHubOriginScope?> acquireScope)
    {
        _processExecutionAdapter = processExecutionAdapter;
        _timeProvider = timeProvider;
        _acquireScope = acquireScope;
    }

    public async Task<GitHubOriginObservation> ObserveAsync(
        string gitExecutablePath, RepositoryRootCandidate candidate, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var root = candidate.CanonicalPath;

        // The root's own administrative scope is held (read-only, no delete sharing) before the first invocation and until
        // the answer is read, so it cannot be moved, replaced by a gitfile or removed to expose a parent repository between
        // the two invocations; an unplain scope is refused without starting any process.
        using var scope = _acquireScope(root);
        if (scope is null)
        {
            return GitHubOriginObservation.Unavailable;
        }

        var shape = await RunAsync(gitExecutablePath, root, ShapeArguments, started, cancellationToken).ConfigureAwait(false);
        if (shape is not { ExitCode: 0 } || !IsOwnMainRepository(shape.StandardOutput, root))
        {
            return GitHubOriginObservation.Unavailable;
        }

        var config = await RunAsync(gitExecutablePath, root, ConfigArguments(root), started, cancellationToken).ConfigureAwait(false);

        return config is null || !scope.IsIntact() ? GitHubOriginObservation.Unavailable : Interpret(config);
    }

    /// <summary>A completed, untruncated exit, or null for every other outcome (timeout, budget exhausted, truncation, launch failure).</summary>
    private async Task<ProcessExecutionResult?> RunAsync(
        string gitExecutablePath, string root, string[] subcommand, long started, CancellationToken cancellationToken)
    {
        var remaining = ObservationBudget - _timeProvider.GetElapsedTime(started);
        if (remaining <= TimeSpan.Zero)
        {
            return null;
        }

        var request = new ProcessExecutionRequest
        {
            ExecutablePath = gitExecutablePath,
            Arguments = [.. HardeningPrefix, .. subcommand],
            WorkingDirectory = root,
            ApprovedRoot = root,
            EnvironmentVariables = ChildEnvironment,
            Timeout = remaining,
            MaxBytesPerStream = MaxBytesPerStream,
            MaxTotalCapturedBytes = MaxTotalCapturedBytes,
        };

        ProcessExecutionResult result;
        try
        {
            result = await _processExecutionAdapter.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception)
        {
            // The OS could not start the vetted executable or enter the project root. Never the exception object or its
            // message: it can carry paths.
            return null;
        }
        catch (ArgumentException exception) when (
            exception.ParamName == nameof(ProcessExecutionRequest.ExecutablePath) && Path.IsPathFullyQualified(gitExecutablePath))
        {
            // The vetted absolute path no longer exists as a file (the executor's own check), identified by parameter, not
            // message. Every other fault is a programming or environment fault and propagates.
            return null;
        }

        if (result.Outcome == ProcessExecutionOutcome.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return result.Outcome == ProcessExecutionOutcome.Exited
            && result.ExitCode.HasValue
            && !result.StandardOutputTruncated
            && !result.StandardErrorTruncated
                ? result
                : null;
    }

    private static bool IsOwnMainRepository(string standardOutput, string root)
    {
        if (!standardOutput.EndsWith('\n'))
        {
            return false;
        }

        var lines = standardOutput[..^1].Split('\n');
        if (lines.Length != 4 || lines[0] != "false")
        {
            return false;
        }

        var ownGitDirectory = Path.TrimEndingDirectorySeparator(Path.Combine(root, ".git"));

        return SamePath(ResolveAgainst(lines[1], root), ownGitDirectory)
            && SamePath(ResolveAgainst(lines[2], root), ownGitDirectory)
            && SamePath(Path.TrimEndingDirectorySeparator(lines[3].Replace('/', '\\')), Path.TrimEndingDirectorySeparator(root));
    }

    private static GitHubOriginObservation Interpret(ProcessExecutionResult result)
    {
        if (result.ExitCode == 1)
        {
            return result.StandardOutput.Length == 0 ? GitHubOriginObservation.NotConfigured : GitHubOriginObservation.Unavailable;
        }

        if (result.ExitCode != 0)
        {
            return GitHubOriginObservation.Unavailable;
        }

        // Exactly one NUL-terminated, non-empty value: a second value (even an identical one) has a second terminator.
        var output = result.StandardOutput;
        var terminator = output.IndexOf('\0');
        if (terminator <= 0 || terminator != output.Length - 1)
        {
            return GitHubOriginObservation.Unavailable;
        }

        var target = GitHubOriginUrlParser.TryParse(output[..terminator]);

        return target is null
            ? GitHubOriginObservation.Unavailable
            : new GitHubOriginObservation(GitHubOriginStatus.Available, target);
    }

    private static string ResolveAgainst(string gitReportedPath, string root) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(gitReportedPath.Replace('/', '\\'), root));

    private static bool SamePath(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
