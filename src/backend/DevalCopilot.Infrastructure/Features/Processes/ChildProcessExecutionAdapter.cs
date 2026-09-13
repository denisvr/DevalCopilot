using System.Diagnostics;
using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;

namespace DevalCopilot.Infrastructure.Features.Processes;

/// <summary>
/// Runs one child process with <see cref="ProcessStartInfo.UseShellExecute"/> disabled and
/// arguments passed through <see cref="ProcessStartInfo.ArgumentList"/> only — there is never
/// a command-line string for a shell to parse, so arguments containing shell metacharacters
/// reach the child exactly as given. This is the foundation adapter for Increment 2: it owns
/// no durable state, attempt, or reconciliation concept — those are wired in a later slice.
/// </summary>
public sealed class ChildProcessExecutionAdapter : IProcessExecutionAdapter
{
    private static readonly TimeSpan PostKillWait = TimeSpan.FromSeconds(5);

    public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var workingDirectory = ResolveWorkingDirectoryWithinApprovedRoot(request.WorkingDirectory, request.ApprovedRoot);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The child receives only this explicit allowlist, never this host process's own
        // ambient environment.
        startInfo.EnvironmentVariables.Clear();
        foreach (var (key, value) in request.EnvironmentVariables)
        {
            startInfo.EnvironmentVariables[key] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        var sharedBudget = new SharedCaptureBudget(request.MaxTotalCapturedBytes);
        var standardOutput = new BoundedOutputCapture(request.MaxBytesPerStream, sharedBudget);
        var standardError = new BoundedOutputCapture(request.MaxBytesPerStream, sharedBudget);

        var stopwatch = Stopwatch.StartNew();
        process.Start();

        var standardOutputDrain = standardOutput.DrainAsync(process.StandardOutput.BaseStream);
        var standardErrorDrain = standardError.DrainAsync(process.StandardError.BaseStream);

        using var timeoutSource = new CancellationTokenSource(request.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        ProcessExecutionOutcome outcome;
        int? exitCode;
        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
            outcome = ProcessExecutionOutcome.Exited;
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            // The caller's own token and the adapter's timeout are linked, so either can land
            // here; the caller's token takes priority when both fired, since cancellation is
            // an explicit request and a timeout is only an implicit consequence of it running
            // out the clock.
            outcome = cancellationToken.IsCancellationRequested
                ? ProcessExecutionOutcome.Cancelled
                : ProcessExecutionOutcome.TimedOut;
            exitCode = null;

            // Kills the entire owned process tree, not just the direct child. Tolerates the
            // narrow race where the process already exited on its own between the
            // WaitForExitAsync cancellation above and this call — an already-terminated tree
            // is a successful outcome here, not a failure. The streams below only reach
            // end-of-stream once every process holding a write handle to them — including any
            // grandchild — has actually terminated, one way or the other.
            ProcessTreeTermination.KillIfStillRunning(process);

            using var postKillTimeout = new CancellationTokenSource(PostKillWait);
            try
            {
                await process.WaitForExitAsync(postKillTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Best-effort: the tree was asked to terminate. Proceed and report what was
                // captured rather than hang the caller indefinitely on a stuck kill.
            }
        }

        stopwatch.Stop();
        await Task.WhenAll(standardOutputDrain, standardErrorDrain).ConfigureAwait(false);

        return new ProcessExecutionResult
        {
            Outcome = outcome,
            ExitCode = exitCode,
            StandardOutput = standardOutput.Text,
            StandardOutputTruncated = standardOutput.Truncated,
            StandardError = standardError.Text,
            StandardErrorTruncated = standardError.Truncated,
            Duration = stopwatch.Elapsed,
        };
    }

    /// <summary>
    /// All checks a caller can get wrong without any process ever starting: an absolute,
    /// existing executable; an absolute approved root; bounded argument count and size; a
    /// positive-or-infinite timeout; and non-negative capture caps within the allowed ceiling.
    /// Fulfils the request contract's "bounded typed arguments" as an enforced boundary rather
    /// than only documentation.
    /// </summary>
    private static void ValidateRequest(ProcessExecutionRequest request)
    {
        if (!Path.IsPathFullyQualified(request.ExecutablePath))
        {
            throw new ArgumentException(
                $"Executable path '{request.ExecutablePath}' must be an absolute path.", nameof(request.ExecutablePath));
        }

        if (!File.Exists(request.ExecutablePath))
        {
            throw new ArgumentException(
                $"Executable path '{request.ExecutablePath}' does not exist as a file.", nameof(request.ExecutablePath));
        }

        if (!Path.IsPathFullyQualified(request.ApprovedRoot))
        {
            throw new ArgumentException(
                $"Approved root '{request.ApprovedRoot}' must be an absolute path.", nameof(request.ApprovedRoot));
        }

        if (request.Arguments.Count > ProcessExecutionRequest.MaxArgumentCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Arguments),
                request.Arguments.Count,
                $"Argument count must not exceed {ProcessExecutionRequest.MaxArgumentCount}.");
        }

        for (var index = 0; index < request.Arguments.Count; index++)
        {
            var byteCount = Encoding.UTF8.GetByteCount(request.Arguments[index]);
            if (byteCount > ProcessExecutionRequest.MaxArgumentUtf8Bytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.Arguments),
                    byteCount,
                    $"Argument at index {index} is {byteCount} UTF-8 bytes, exceeding the maximum of " +
                    $"{ProcessExecutionRequest.MaxArgumentUtf8Bytes}.");
            }
        }

        if (request.Timeout != Timeout.InfiniteTimeSpan && request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Timeout), request.Timeout, "Timeout must be positive or Timeout.InfiniteTimeSpan.");
        }

        if (request.MaxBytesPerStream < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.MaxBytesPerStream), request.MaxBytesPerStream, "Must not be negative.");
        }

        if (request.MaxTotalCapturedBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.MaxTotalCapturedBytes), request.MaxTotalCapturedBytes, "Must not be negative.");
        }

        if (request.MaxTotalCapturedBytes > ProcessExecutionRequest.MaxAllowedTotalCapturedBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.MaxTotalCapturedBytes),
                request.MaxTotalCapturedBytes,
                $"Must not exceed {ProcessExecutionRequest.MaxAllowedTotalCapturedBytes}.");
        }
    }

    private static string ResolveWorkingDirectoryWithinApprovedRoot(string workingDirectory, string approvedRoot)
    {
        // Path.TrimEndingDirectorySeparator is boundary-safe for a filesystem-root approved
        // root: it only trims a separator that lies beyond the root, so "C:\" (the root
        // itself) is left as "C:\", never collapsed to the drive-relative "C:". A blind
        // TrimEnd of separator characters would do exactly that and silently break
        // containment checks against a root-level approved root.
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(approvedRoot));
        var canonicalWorkingDirectory = Path.GetFullPath(workingDirectory);

        if (!IsWithinRoot(canonicalWorkingDirectory, canonicalRoot))
        {
            throw new ArgumentException(
                $"Working directory '{workingDirectory}' must remain beneath the approved root '{approvedRoot}'.",
                nameof(workingDirectory));
        }

        return canonicalWorkingDirectory;
    }

    private static bool IsWithinRoot(string canonicalPath, string canonicalRoot)
    {
        if (string.Equals(canonicalPath, canonicalRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // canonicalRoot already ends with a separator when it is itself a filesystem root
        // (e.g. "C:\"), and never does otherwise (Path.TrimEndingDirectorySeparator strips
        // exactly that case) — so appending one only when missing yields the correct prefix
        // for both shapes without ever double-separating a root.
        var rootWithSeparator = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;

        return canonicalPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
