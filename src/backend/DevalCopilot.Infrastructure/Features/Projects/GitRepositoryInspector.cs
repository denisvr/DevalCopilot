using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// Answers only Git-specific questions about an already filesystem-validated candidate root.
/// Composed entirely on top of the existing <see cref="IProcessExecutionAdapter"/> boundary —
/// the same way <c>ToolDiscoveryAdapter</c> is — never a second child-process execution path.
///
/// <para>
/// Every invocation is fixed, bounded, argument-list-only, and read-only: exactly six Git
/// subcommands, never a shell, never a command line built from anything but literal constants
/// plus the already-validated <see cref="RepositoryRootCandidate.CanonicalPath"/> as the working
/// directory. No remote, config-write, log, diff, or mutation command is ever invoked, and Git
/// aliases, hooks, and credential prompts are never reachable by this command set (see the
/// per-command notes below).
/// </para>
/// </summary>
public sealed class GitRepositoryInspector(IProcessExecutionAdapter processExecutionAdapter) : IGitRepositoryInspector
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private const int MaxCapturedBytes = 4 * 1024;

    /// <summary>
    /// Fixed hardening flags applied before every subcommand, regardless of what the target
    /// repository's own (untrusted) <c>.git/config</c> requests:
    /// <list type="bullet">
    /// <item><description><c>--no-pager</c> — belt-and-suspenders against ever invoking a
    /// pager subprocess; moot in practice since stdout is always redirected to a pipe, never a
    /// TTY, which is itself sufficient for Git to skip paging.</description></item>
    /// <item><description><c>-c core.fsmonitor=false</c> — empirically verified necessary: a
    /// repository-local <c>core.fsmonitor</c> value is executed as a command by <c>git
    /// status</c>. This override, supplied last and therefore highest-precedence, prevents
    /// that regardless of what the repository's own config sets it to.</description></item>
    /// <item><description><c>-c core.untrackedCache=false</c> — prevents the untracked-cache
    /// mechanism from writing cache extension data to the index during inspection.</description></item>
    /// <item><description><c>-c protocol.allow=never</c> — these commands never touch a
    /// remote, but this removes any protocol-handler surface entirely as a defense-in-depth
    /// measure (relevant if an unusual submodule configuration were ever present).</description></item>
    /// </list>
    /// Git aliases are deliberately not neutralized here: empirically verified (a repository
    /// config defining <c>alias.status</c> to an arbitrary command has no effect on <c>git
    /// status</c>) that a same-named alias cannot shadow one of Git's own builtin subcommands,
    /// which is all this inspector ever invokes.
    /// </summary>
    private static readonly IReadOnlyList<string> HardeningPrefix =
        ["--no-pager", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false", "-c", "protocol.allow=never"];

    /// <summary><c>GIT_TERMINAL_PROMPT=0</c> — none of these commands should ever need to
    /// authenticate, but this guarantees a failure instead of a hang if something unexpected
    /// tries to prompt. <c>GIT_OPTIONAL_LOCKS=0</c> — makes the read-only guarantee operational:
    /// <c>git status</c> cannot refresh or write the index or other optional lock-guarded
    /// state.</summary>
    private static readonly IReadOnlyDictionary<string, string> HardeningEnvironment =
        new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_OPTIONAL_LOCKS"] = "0" };

    /// <summary>Empirically verified exit code for <c>symbolic-ref -q --short HEAD</c> when
    /// <c>HEAD</c> is detached (with <c>-q</c> suppressing the error message, so stdout is also
    /// empty in this case).</summary>
    private const int SymbolicRefDetachedExitCode = 1;

    /// <summary>Empirically verified exit code for <c>rev-parse HEAD</c> when the branch is
    /// unborn (no commit exists yet). Git still echoes the literal, unresolvable "HEAD"
    /// argument to stdout in this case (its documented behavior for disambiguating a revision
    /// from a path when no commit exists to resolve it against) — this is not empty output, and
    /// is exactly the fixed string below, never a fact this method infers from "no output".</summary>
    private const int RevParseHeadUnbornExitCode = 128;

    private const string RevParseHeadUnbornOutput = "HEAD";

    public async Task<GitRepositoryInspectionResult> InspectAsync(
        RepositoryRootCandidate candidate, CancellationToken cancellationToken)
    {
        var descriptor = HostCapabilityCatalog.Get(Capability.Git);
        var resolvedGitPath = HostExecutableResolver.TryResolve(descriptor.CandidateExecutableNames, descriptor.FallbackDirectories);
        if (resolvedGitPath is null)
        {
            return Failure(GitRepositoryInspectionOutcome.GitUnavailable);
        }

        var gitDir = await RunAsync(resolvedGitPath, candidate.CanonicalPath, ["rev-parse", "--git-dir"], cancellationToken);
        if (!Succeeded(gitDir))
        {
            return Failure(gitDir.Outcome == GitCommandOutcome.TimedOut
                ? GitRepositoryInspectionOutcome.GitInvocationTimedOut
                : GitRepositoryInspectionOutcome.NotAGitRepository);
        }

        var isBare = await RunAsync(resolvedGitPath, candidate.CanonicalPath, ["rev-parse", "--is-bare-repository"], cancellationToken);
        if (!Succeeded(isBare))
        {
            return Failure(isBare.Outcome == GitCommandOutcome.TimedOut
                ? GitRepositoryInspectionOutcome.GitInvocationTimedOut
                : GitRepositoryInspectionOutcome.NotAGitRepository);
        }

        if (string.Equals(isBare.Output, "true", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(GitRepositoryInspectionOutcome.BareRepositoryNotSupported);
        }

        var showToplevel = await RunAsync(resolvedGitPath, candidate.CanonicalPath, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (!Succeeded(showToplevel))
        {
            return Failure(showToplevel.Outcome == GitCommandOutcome.TimedOut
                ? GitRepositoryInspectionOutcome.GitInvocationTimedOut
                : GitRepositoryInspectionOutcome.NotAGitRepository);
        }

        if (!PathsMatch(showToplevel.Output, candidate.CanonicalPath))
        {
            return Failure(GitRepositoryInspectionOutcome.NotTopLevelRoot);
        }

        var gitCommonDir = await RunAsync(resolvedGitPath, candidate.CanonicalPath, ["rev-parse", "--git-common-dir"], cancellationToken);
        if (!Succeeded(gitCommonDir))
        {
            return Failure(gitCommonDir.Outcome == GitCommandOutcome.TimedOut
                ? GitRepositoryInspectionOutcome.GitInvocationTimedOut
                : GitRepositoryInspectionOutcome.NotAGitRepository);
        }

        if (!PathsMatch(ResolveAgainst(gitDir.Output, candidate.CanonicalPath), ResolveAgainst(gitCommonDir.Output, candidate.CanonicalPath)))
        {
            return Failure(GitRepositoryInspectionOutcome.LinkedWorktreeNotSupported);
        }

        // Bounded consistency check: brackets the one call whose cost scales with working-tree
        // size (status) with a HEAD-signature read on both sides. A concurrent commit/checkout
        // in another terminal during that window is the only realistic mid-inspection change;
        // structural facts checked above (bare-ness, worktree linkage) do not change on a human
        // timescale during a few-hundred-millisecond inspection and are not re-checked here.
        // An invalid/corrupt HEAD state fails closed immediately, without retrying — that is a
        // structural problem with the repository, not a transient race the retry exists for.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var before = await CaptureHeadSignatureAsync(resolvedGitPath, candidate.CanonicalPath, cancellationToken);
            if (before.Outcome == HeadSignatureOutcome.TimedOut)
            {
                return Failure(GitRepositoryInspectionOutcome.GitInvocationTimedOut);
            }

            if (before.Outcome == HeadSignatureOutcome.Invalid)
            {
                return Failure(GitRepositoryInspectionOutcome.InvalidHeadState);
            }

            var status = await RunAsync(resolvedGitPath, candidate.CanonicalPath, ["status", "--porcelain=v1"], cancellationToken);
            if (!Succeeded(status))
            {
                return Failure(status.Outcome == GitCommandOutcome.TimedOut
                    ? GitRepositoryInspectionOutcome.GitInvocationTimedOut
                    : GitRepositoryInspectionOutcome.NotAGitRepository);
            }

            var after = await CaptureHeadSignatureAsync(resolvedGitPath, candidate.CanonicalPath, cancellationToken);
            if (after.Outcome == HeadSignatureOutcome.TimedOut)
            {
                return Failure(GitRepositoryInspectionOutcome.GitInvocationTimedOut);
            }

            if (after.Outcome == HeadSignatureOutcome.Invalid)
            {
                return Failure(GitRepositoryInspectionOutcome.InvalidHeadState);
            }

            if (before.Signature == after.Signature)
            {
                var isDirty = status.Output.Length > 0;
                return new GitRepositoryInspectionResult(
                    GitRepositoryInspectionOutcome.Success, after.HeadState, after.BranchName, after.HeadCommitSha, isDirty);
            }
        }

        return Failure(GitRepositoryInspectionOutcome.RepositoryChangedDuringInspection);
    }

    private enum HeadSignatureOutcome
    {
        Valid,
        Invalid,
        TimedOut,
    }

    private readonly record struct HeadSignatureCapture(
        HeadSignatureOutcome Outcome, string Signature, RepositoryHeadState? HeadState, string? BranchName, string? HeadCommitSha);

    private static readonly HeadSignatureCapture TimedOutSignature = new(HeadSignatureOutcome.TimedOut, string.Empty, null, null, null);
    private static readonly HeadSignatureCapture InvalidSignature = new(HeadSignatureOutcome.Invalid, string.Empty, null, null, null);

    /// <summary>
    /// Classifies <c>HEAD</c> against exactly three valid combinations, derived from real Git
    /// semantics and empirically verified exit codes/output shapes — never inferred from "did
    /// this command merely fail":
    /// <list type="bullet">
    /// <item><description><c>symbolic-ref</c> exits 0 (a branch name) and <c>rev-parse HEAD</c>
    /// exits 0 (a commit) → <see cref="RepositoryHeadState.OnBranch"/>.</description></item>
    /// <item><description><c>symbolic-ref</c> exits 0 and <c>rev-parse HEAD</c> exits exactly
    /// 128 with stdout exactly <c>"HEAD"</c> (the unborn-branch shape — Git echoes the literal
    /// unresolvable argument) → <see cref="RepositoryHeadState.Unborn"/>.</description></item>
    /// <item><description><c>symbolic-ref</c> exits exactly 1 with empty output (the detached
    /// shape, <c>-q</c> suppresses the error text) and <c>rev-parse HEAD</c> exits 0 →
    /// <see cref="RepositoryHeadState.Detached"/>.</description></item>
    /// </list>
    /// Both commands failing, either command launching but exiting with any other code, or
    /// either producing an unexpected output shape for its failure case, is not a state this
    /// method will fabricate a result for — it reports <see cref="HeadSignatureOutcome.Invalid"/>
    /// instead, and the caller fails the whole inspection closed rather than persist anything.
    /// </summary>
    private async Task<HeadSignatureCapture> CaptureHeadSignatureAsync(
        string resolvedGitPath, string workingDirectory, CancellationToken cancellationToken)
    {
        var symbolicRef = await RunAsync(resolvedGitPath, workingDirectory, ["symbolic-ref", "-q", "--short", "HEAD"], cancellationToken);
        if (symbolicRef.Outcome == GitCommandOutcome.TimedOut)
        {
            return TimedOutSignature;
        }

        var revParseHead = await RunAsync(resolvedGitPath, workingDirectory, ["rev-parse", "HEAD"], cancellationToken);
        if (revParseHead.Outcome == GitCommandOutcome.TimedOut)
        {
            return TimedOutSignature;
        }

        // Only a genuine process exit (not a launch failure) ever qualifies below — a launch
        // failure carries no meaningful exit code to classify.
        if (symbolicRef.Outcome != GitCommandOutcome.Exited || revParseHead.Outcome != GitCommandOutcome.Exited)
        {
            return InvalidSignature;
        }

        var symbolicRefIsOnBranch = symbolicRef.ExitCode == 0 && symbolicRef.Output.Length > 0;
        var symbolicRefIsExpectedDetached = symbolicRef.ExitCode == SymbolicRefDetachedExitCode && symbolicRef.Output.Length == 0;
        var revParseIsResolved = revParseHead.ExitCode == 0 && revParseHead.Output.Length > 0;
        var revParseIsExpectedUnborn =
            revParseHead.ExitCode == RevParseHeadUnbornExitCode
            && string.Equals(revParseHead.Output, RevParseHeadUnbornOutput, StringComparison.Ordinal);

        if (symbolicRefIsOnBranch && revParseIsResolved)
        {
            return new HeadSignatureCapture(
                HeadSignatureOutcome.Valid, $"{symbolicRef.Output}\0{revParseHead.Output}",
                RepositoryHeadState.OnBranch, symbolicRef.Output, revParseHead.Output);
        }

        if (symbolicRefIsOnBranch && revParseIsExpectedUnborn)
        {
            return new HeadSignatureCapture(
                HeadSignatureOutcome.Valid, $"{symbolicRef.Output}\0",
                RepositoryHeadState.Unborn, symbolicRef.Output, null);
        }

        if (symbolicRefIsExpectedDetached && revParseIsResolved)
        {
            return new HeadSignatureCapture(
                HeadSignatureOutcome.Valid, $"\0{revParseHead.Output}",
                RepositoryHeadState.Detached, null, revParseHead.Output);
        }

        // Both failed, or one succeeded in a shape neither valid combination above accounts
        // for (e.g. an unexpected exit code, or output present where none is expected) — never
        // fabricated as Detached or Unborn.
        return InvalidSignature;
    }

    private enum GitCommandOutcome
    {
        Exited,
        TimedOut,
        LaunchFailed,
    }

    /// <summary><paramref name="ExitCode"/> is only meaningful when <paramref name="Outcome"/>
    /// is <see cref="GitCommandOutcome.Exited"/>.</summary>
    private readonly record struct GitCommandResult(GitCommandOutcome Outcome, int ExitCode, string Output);

    private static bool Succeeded(GitCommandResult result) => result.Outcome == GitCommandOutcome.Exited && result.ExitCode == 0;

    private async Task<GitCommandResult> RunAsync(
        string resolvedGitPath, string workingDirectory, IReadOnlyList<string> subcommandArguments, CancellationToken cancellationToken)
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
            Timeout = ProbeTimeout,
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
            // The adapter rejected the request or could not start git at all. Never the
            // exception object or its message — only a closed outcome leaves this method.
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
    private static bool PathsMatch(string gitReportedPath, string canonicalPath) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(gitReportedPath.Replace('/', '\\')),
            Path.TrimEndingDirectorySeparator(canonicalPath),
            StringComparison.OrdinalIgnoreCase);

    /// <summary><c>--git-dir</c>/<c>--git-common-dir</c> print a path relative to the working
    /// directory for the common case (e.g. <c>.git</c>) and an absolute path for a linked
    /// worktree — resolved to a common absolute, slash-normalized form before comparison either
    /// way.</summary>
    private static string ResolveAgainst(string gitReportedPath, string workingDirectory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(gitReportedPath.Replace('/', '\\'), workingDirectory));

    private static GitRepositoryInspectionResult Failure(GitRepositoryInspectionOutcome outcome) =>
        new(outcome, null, null, null, false);
}
