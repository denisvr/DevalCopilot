using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

namespace DevalCopilot.Infrastructure.Features.Projects;

/// <summary>
/// Captures a bounded, internally consistent view of a linked workspace. The reader brackets
/// status, diff, and untracked-file hashes with repeat observations and discards the whole
/// result when anything changes during capture. Git receives only fixed builtin subcommands;
/// no pager, external diff, text conversion, protocol, prompt, optional lock, fsmonitor, or
/// untracked-cache behavior is available to repository configuration.
/// </summary>
public sealed class GitWorkspaceEvidenceReader(IProcessExecutionAdapter processExecutionAdapter) : IGitWorkspaceEvidenceReader
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);
    private const int MaxCapturedBytes = 512 * 1024;
    private const int MaxChangedPaths = 128;

    private static readonly IReadOnlyList<string> HardeningPrefix =
    [
        "--no-pager", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false",
        "-c", "protocol.allow=never", "-c", "core.attributesfile=NUL",
    ];

    private static readonly IReadOnlyDictionary<string, string> HardeningEnvironment =
        new Dictionary<string, string>
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_OPTIONAL_LOCKS"] = "0",
            ["GIT_CONFIG_NOSYSTEM"] = "1",
        };

    public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var descriptor = HostCapabilityCatalog.Get(Capability.Git);
        var gitPath = HostExecutableResolver.TryResolve(descriptor.CandidateExecutableNames, descriptor.FallbackDirectories);
        if (gitPath is null)
        {
            return Failure(GitWorkspaceEvidenceOutcome.GitUnavailable);
        }

        for (var captureAttempt = 0; captureAttempt < 2; captureAttempt++)
        {
            var beforeHead = await RunAsync(gitPath, workspacePath, ["rev-parse", "HEAD"], cancellationToken);
            var beforeStatus = await RunAsync(gitPath, workspacePath, ["status", "--porcelain=v1", "-z", "--no-renames", "--untracked-files=all"], cancellationToken);
            var firstDiff = await RunAsync(gitPath, workspacePath, DiffArguments, cancellationToken);

            var earlyFailure = MapFailure(beforeHead, beforeStatus, firstDiff);
            if (earlyFailure is not null)
            {
                return Failure(earlyFailure.Value);
            }

            var beforeHeadSha = beforeHead.Output.Trim();
            if (!IsFullSha(beforeHeadSha))
            {
                return Failure(GitWorkspaceEvidenceOutcome.InvalidGitState);
            }

            var changedPaths = ParseChangedPaths(beforeStatus.Output);
            if (changedPaths is null)
            {
                return Failure(GitWorkspaceEvidenceOutcome.EvidenceTooLarge);
            }

            var untrackedHashes = new List<(string Path, string Hash)>();
            foreach (var changedPath in changedPaths.Where(path => path.IndexStatus == "?" && path.WorkTreeStatus == "?"))
            {
                var hash = await RunAsync(gitPath, workspacePath, ["hash-object", "--no-filters", "--", changedPath.Path], cancellationToken);
                var hashFailure = MapFailure(hash);
                if (hashFailure is not null)
                {
                    return Failure(hashFailure.Value);
                }

                var trimmedHash = hash.Output.Trim();
                if (!IsFullSha(trimmedHash))
                {
                    return Failure(GitWorkspaceEvidenceOutcome.InvalidGitState);
                }

                untrackedHashes.Add((changedPath.Path, trimmedHash));
            }

            var afterStatus = await RunAsync(gitPath, workspacePath, ["status", "--porcelain=v1", "-z", "--no-renames", "--untracked-files=all"], cancellationToken);
            var afterHead = await RunAsync(gitPath, workspacePath, ["rev-parse", "HEAD"], cancellationToken);
            var secondDiff = await RunAsync(gitPath, workspacePath, DiffArguments, cancellationToken);

            var lateFailure = MapFailure(afterStatus, afterHead, secondDiff);
            if (lateFailure is not null)
            {
                return Failure(lateFailure.Value);
            }

            var afterHeadSha = afterHead.Output.Trim();
            if (!IsFullSha(afterHeadSha))
            {
                return Failure(GitWorkspaceEvidenceOutcome.InvalidGitState);
            }

            if (beforeHeadSha == afterHeadSha && beforeStatus.Output == afterStatus.Output && firstDiff.Output == secondDiff.Output)
            {
                var fingerprint = ComputeFingerprint(beforeHeadSha, beforeStatus.Output, secondDiff.Output, untrackedHashes);
                return new GitWorkspaceEvidenceResult(
                    GitWorkspaceEvidenceOutcome.Success,
                    beforeHeadSha,
                    fingerprint,
                    changedPaths,
                    secondDiff.Output);
            }
        }

        return Failure(GitWorkspaceEvidenceOutcome.RepositoryChangedDuringCapture);
    }

    private static readonly IReadOnlyList<string> DiffArguments =
        ["diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--binary", "HEAD"];

    private async Task<GitCommandResult> RunAsync(
        string gitPath, string workspacePath, IReadOnlyList<string> subcommandArguments, CancellationToken cancellationToken)
    {
        var arguments = new List<string>(HardeningPrefix.Count + subcommandArguments.Count);
        arguments.AddRange(HardeningPrefix);
        arguments.AddRange(subcommandArguments);

        ProcessExecutionResult result;
        try
        {
            result = await processExecutionAdapter.ExecuteAsync(new ProcessExecutionRequest
            {
                ExecutablePath = gitPath,
                Arguments = arguments,
                WorkingDirectory = workspacePath,
                ApprovedRoot = workspacePath,
                Timeout = ReadTimeout,
                MaxBytesPerStream = MaxCapturedBytes,
                MaxTotalCapturedBytes = MaxCapturedBytes,
                EnvironmentVariables = HardeningEnvironment,
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new GitCommandResult(GitCommandOutcome.LaunchFailed, 0, string.Empty, false);
        }

        return result.Outcome switch
        {
            ProcessExecutionOutcome.Cancelled => throw new OperationCanceledException(cancellationToken),
            ProcessExecutionOutcome.TimedOut => new GitCommandResult(GitCommandOutcome.TimedOut, 0, string.Empty, false),
            _ => new GitCommandResult(
                GitCommandOutcome.Exited,
                result.ExitCode!.Value,
                result.StandardOutput,
                result.StandardOutputTruncated || result.StandardErrorTruncated),
        };
    }

    private static GitWorkspaceEvidenceOutcome? MapFailure(params GitCommandResult[] results)
    {
        foreach (var result in results)
        {
            if (result.Outcome == GitCommandOutcome.TimedOut)
            {
                return GitWorkspaceEvidenceOutcome.GitInvocationTimedOut;
            }

            if (result.Truncated)
            {
                return GitWorkspaceEvidenceOutcome.EvidenceTooLarge;
            }

            if (result.Outcome != GitCommandOutcome.Exited || result.ExitCode != 0)
            {
                return GitWorkspaceEvidenceOutcome.GitInvocationFailed;
            }
        }

        return null;
    }

    private static IReadOnlyList<GitWorkspaceChangedPath>? ParseChangedPaths(string porcelain)
    {
        var result = new List<GitWorkspaceChangedPath>();
        foreach (var record in porcelain.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (record.Length < 4 || record[2] != ' ')
            {
                return null;
            }

            if (result.Count == MaxChangedPaths)
            {
                return null;
            }

            var path = record[3..];
            if (path.Length == 0 || path.Length > 4096)
            {
                return null;
            }

            result.Add(new GitWorkspaceChangedPath(path, null, record[..1], record[1..2]));
        }

        return result;
    }

    private static string ComputeFingerprint(
        string headCommitSha,
        string porcelain,
        string completeDiff,
        IReadOnlyCollection<(string Path, string Hash)> untrackedHashes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "head", headCommitSha);
        Append(hash, "status", porcelain);
        Append(hash, "diff", completeDiff);
        foreach (var (path, fileHash) in untrackedHashes.OrderBy(value => value.Path, StringComparer.Ordinal))
        {
            Append(hash, "untracked-path", path);
            Append(hash, "untracked-hash", fileHash);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string label, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(label));
        hash.AppendData([0]);
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static bool IsFullSha(string value) =>
        value.Length == 40 && value.All(character => char.IsAsciiHexDigit(character));

    private static GitWorkspaceEvidenceResult Failure(GitWorkspaceEvidenceOutcome outcome) =>
        new(outcome, null, null, [], null);

    private enum GitCommandOutcome
    {
        Exited,
        TimedOut,
        LaunchFailed,
    }

    private readonly record struct GitCommandResult(GitCommandOutcome Outcome, int ExitCode, string Output, bool Truncated);
}
