using DevalCopilot.Application.Features.Processes.Ports;

namespace DevalCopilot.Infrastructure.Features.Runs;

internal enum LocalCommitGitOutcome
{
    Exited,
    TimedOut,
    LaunchFailed,
}

internal sealed record LocalCommitGitResult(LocalCommitGitOutcome Outcome, int ExitCode, string Output, bool Truncated)
{
    public bool Succeeded => Outcome == LocalCommitGitOutcome.Exited && ExitCode == 0 && !Truncated;
}

/// <summary>
/// Runs every Git invocation of the explicit local commit (ADR-0029) with a fixed argument list, a controlled environment, bounded
/// capture and timeouts, and the same hardening on each call: no pager, fsmonitor, untracked cache or network, no global or system
/// configuration, no global attributes, signing disabled, replacement objects ignored, and an owned, proven-empty hooks directory
/// so neither a commit hook nor the <c>reference-transaction</c> hook can run. The caller supplies only the subcommand arguments.
/// </summary>
internal sealed class LocalCommitGitRunner(IProcessExecutionAdapter processExecutionAdapter, LocalCommitStorage storage)
{
    public const int MaxCapturedBytes = 512 * 1024;

    private static readonly string[] UserConfigurationLocationVariables =
        ["USERPROFILE", "HOME", "HOMEDRIVE", "HOMEPATH", "XDG_CONFIG_HOME", "APPDATA"];

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(30);

    public async Task<LocalCommitGitResult> RunAsync(
        string gitPath,
        string workspacePath,
        IReadOnlyList<string> subcommandArguments,
        CancellationToken cancellationToken,
        byte[]? standardInput = null,
        IReadOnlyDictionary<string, string>? extraEnvironment = null,
        bool mutation = false,
        bool keepUserConfiguration = false,
        string? attributeSourceTree = null,
        string? provenGitDirectory = null)
    {
        var arguments = new List<string>
        {
            "--no-pager",
            "--literal-pathspecs",
            "-c", "core.fsmonitor=false",
            "-c", "core.untrackedCache=false",
            "-c", "protocol.allow=never",
            "-c", "core.attributesFile=" + ForwardSlashes(storage.EmptyConfigPath),
            "-c", "core.hooksPath=" + ForwardSlashes(storage.HooksDirectory),
            "-c", "commit.gpgsign=false",
            "-c", "tag.gpgsign=false",
            "-c", "i18n.commitEncoding=utf-8",
            "-c", "gc.auto=0",
            "-c", "core.quotePath=false",
        };
        if (provenGitDirectory is not null)
        {
            // The caller proved this administrative directory through physical ownership. Naming it explicitly keeps a proof read
            // from rediscovering a repository through a workspace `.git` file that an outside writer may have redirected.
            arguments.Add("--git-dir=" + ForwardSlashes(provenGitDirectory));
            arguments.Add("--work-tree=" + ForwardSlashes(workspacePath));
        }

        if (attributeSourceTree is not null)
        {
            arguments.Add("--attr-source=" + attributeSourceTree);
        }

        arguments.AddRange(subcommandArguments);

        var environment = new Dictionary<string, string>
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_OPTIONAL_LOCKS"] = "0",
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_ATTR_NOSYSTEM"] = "1",
            ["GIT_NO_REPLACE_OBJECTS"] = "1",
            ["GIT_NO_LAZY_FETCH"] = "1",
        };
        if (!keepUserConfiguration)
        {
            environment["GIT_CONFIG_GLOBAL"] = storage.EmptyConfigPath;
        }
        else
        {
            // Only the author identity is ever read from the user's own configuration, so only the variables that locate it pass
            // through; every mutating command runs against an empty global configuration instead.
            foreach (var name in UserConfigurationLocationVariables)
            {
                if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
                {
                    environment[name] = value;
                }
            }
        }

        if (extraEnvironment is not null)
        {
            foreach (var (name, value) in extraEnvironment)
            {
                environment[name] = value;
            }
        }

        try
        {
            var result = await processExecutionAdapter.ExecuteAsync(
                new ProcessExecutionRequest
                {
                    ExecutablePath = gitPath,
                    Arguments = arguments,
                    WorkingDirectory = workspacePath,
                    ApprovedRoot = workspacePath,
                    Timeout = mutation ? MutationTimeout : ReadTimeout,
                    MaxBytesPerStream = MaxCapturedBytes,
                    MaxTotalCapturedBytes = MaxCapturedBytes,
                    EnvironmentVariables = environment,
                    StandardInput = standardInput,
                },
                cancellationToken);

            return result.Outcome switch
            {
                ProcessExecutionOutcome.Cancelled => throw new OperationCanceledException(cancellationToken),
                ProcessExecutionOutcome.TimedOut => new LocalCommitGitResult(LocalCommitGitOutcome.TimedOut, 0, string.Empty, false),
                _ => new LocalCommitGitResult(
                    LocalCommitGitOutcome.Exited,
                    result.ExitCode!.Value,
                    result.StandardOutput,
                    result.StandardOutputTruncated || result.StandardErrorTruncated),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new LocalCommitGitResult(LocalCommitGitOutcome.LaunchFailed, 0, string.Empty, false);
        }
    }

    private static string ForwardSlashes(string path) => path.Replace('\\', '/');
}
