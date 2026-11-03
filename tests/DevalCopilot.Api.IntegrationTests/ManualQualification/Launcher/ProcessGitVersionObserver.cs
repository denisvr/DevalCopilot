using System.Diagnostics;
using System.Text;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The real observation of the approved Git's version: the exact file, the single argument <c>--version</c>, an environment holding at
/// most <c>SystemRoot</c>, no shell. One monotonic deadline covers the process's exit and the completion of BOTH redirected streams, so
/// a slow exit, a pipe that never ends (for instance one held open by a descendant) and a stream that keeps writing all end the same
/// observation; there is no second observation wait, retry or fallback. Each stream is read in small blocks and at most
/// <see cref="GitVersionGate.MaximumOutputBytes"/> bytes of it are kept, so nothing is buffered without bound; the first byte beyond
/// the cap on either stream makes the observation oversized and stops it. Standard error is only counted against its cap and
/// discarded, standard output is returned to the gate, and neither is ever persisted or logged.
/// Expiry is terminal: the observation is accepted only when the wait itself established that the process exited and both streams
/// reached their end without overflow AND the absolute monotonic deadline was still open when that result was read. A wait that timed
/// out, that had no time left to start, or that returned a success index after the deadline, refuses the observation even if
/// everything happened to finish before the next status read. The process facts (exit, code) are sampled at the decision; the I/O and
/// termination facts are measured after the one cleanup, and a process that could not be started reports no completed I/O. The failure facts are independent: overflow
/// (<c>Truncated</c>), unfinished I/O (<c>IoComplete</c>, true only when both readers successfully reached the end of their stream)
/// and unproven termination (the kill could not be confirmed, or the root exited while a pipe never ended, so a descendant may still
/// hold it) are each reported on their own. Cleanup has one separate, best-effort budget of 250 ms for the operating system to confirm
/// the kill and for the readers to finish; it is not part of the observation and never a second full wait.
/// </summary>
public sealed class ProcessGitVersionObserver : IGitVersionObserver
{
    /// <summary>The one best-effort cleanup budget, shared by the kill confirmation and the readers' last chance to finish.</summary>
    private static readonly TimeSpan CleanupConfirmation = TimeSpan.FromMilliseconds(250);

    private const int ReadBlockBytes = 256;

    private readonly Func<Task[], TimeSpan, int> _wait;

    public ProcessGitVersionObserver()
        : this(static (tasks, remaining) => Task.WaitAny(tasks, remaining))
    {
    }

    /// <summary>Test seam: the one wait of the observation. It receives the completion task and the oversize signal and the time left,
    /// and returns the index of the task that finished first, or -1 when the time elapsed. Nothing else is replaceable.</summary>
    internal ProcessGitVersionObserver(Func<Task[], TimeSpan, int> wait)
    {
        _wait = wait;
    }

    public IReadOnlyList<string> EnvironmentNames =>
        Environment.GetEnvironmentVariable("SystemRoot") is { Length: > 0 } ? ["SystemRoot"] : [];

    public GitVersionObservation Observe(string gitPath, string workingDirectory, TimeSpan bound)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var start = new ProcessStartInfo(gitPath, ["--version"])
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.Environment.Clear();
            if (Environment.GetEnvironmentVariable("SystemRoot") is { Length: > 0 } systemRoot)
            {
                start.Environment["SystemRoot"] = systemRoot;
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return new GitVersionObservation(false, null, false, false, string.Empty, IoComplete: false);
            }

            var oversized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var output = ReadBoundedAsync(process.StandardOutput.BaseStream, oversized);
            var error = ReadBoundedAsync(process.StandardError.BaseStream, oversized);
            var all = Task.WhenAll(output, error, process.WaitForExitAsync());

            // The wait's own answer decides, but only while the absolute deadline is still open when it is read: a wait that timed
            // out (or had no time left to start) is expired, and so is a success index returned after the monotonic deadline (a caller
            // descheduled between the relative timeout and the wait). Expiry is terminal whatever the status reads below then show.
            var remaining = bound - clock.Elapsed;
            var first = remaining > TimeSpan.Zero ? _wait([all, oversized.Task], remaining) : -1;
            var expired = first < 0 || clock.Elapsed >= bound;
            var wasOversized = oversized.Task.IsCompleted;
            if (!expired && first == 0 && !wasOversized && Ended(output) && Ended(error) && all.IsCompletedSuccessfully)
            {
                return new GitVersionObservation(
                    true, process.ExitCode, false, false, Encoding.UTF8.GetString(output.Result.Bytes));
            }

            // Not an accepted observation: stop whatever still runs, once, and say honestly what could be proven.
            var exitedAtDecision = process.HasExited;
            int? exitCodeAtDecision = exitedAtDecision ? process.ExitCode : null;
            var cleanup = Stopwatch.StartNew();
            var killUnconfirmed = Terminate(process, cleanup);
            if (!Ended(output) || !Ended(error))
            {
                var left = CleanupConfirmation - cleanup.Elapsed;
                if (left > TimeSpan.Zero)
                {
                    Task.WaitAll([output, error], left);
                }
            }

            // The process facts are those at the decision (before the kill); the closure facts are those after the one cleanup.
            var ioComplete = Ended(output) && Ended(error);
            return new GitVersionObservation(
                exitedAtDecision,
                exitedAtDecision ? exitCodeAtDecision : null,
                expired,
                wasOversized,
                string.Empty,
                IoComplete: ioComplete,
                TerminationUnproven: killUnconfirmed || (process.HasExited && !ioComplete));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new GitVersionObservation(false, null, false, false, string.Empty, IoComplete: false);
        }
    }

    /// <summary>True only when the reader finished normally and reached the end of its stream: a read failure, a stream still open
    /// and an overflow all leave this false.</summary>
    private static bool Ended(Task<StreamRead> read) => read.IsCompletedSuccessfully && read.Result.Ended;

    /// <summary>Kills the child and its tree if it still runs and waits, within the shared cleanup budget, for the operating system to
    /// confirm it. True when that could not be confirmed.</summary>
    private static bool Terminate(Process process, Stopwatch cleanup)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            var left = CleanupConfirmation - cleanup.Elapsed;
            return !process.WaitForExit(left > TimeSpan.Zero ? left : TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return true;
        }
    }

    /// <summary>Reads one stream in small blocks, keeping at most the cap and signalling the first byte beyond it. A read failure ends
    /// the read as not ended. The kept bytes are returned, never logged.</summary>
    private static async Task<StreamRead> ReadBoundedAsync(Stream stream, TaskCompletionSource oversized)
    {
        var kept = new byte[GitVersionGate.MaximumOutputBytes];
        var count = 0;
        var block = new byte[ReadBlockBytes];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(block);
                if (read == 0)
                {
                    return new StreamRead(kept[..count], true);
                }

                var take = Math.Min(read, kept.Length - count);
                Buffer.BlockCopy(block, 0, kept, count, take);
                count += take;
                if (read > take)
                {
                    // Overflow is signalled once, the kept bytes stay at the cap and the rest is read and discarded (never stored)
                    // until the stream ends, so whether the stream reaches its end stays knowable independently of the overflow.
                    oversized.TrySetResult();
                    return new StreamRead(kept[..count], await DrainAsync(stream, block));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return new StreamRead(kept[..count], false);
        }
    }

    /// <summary>Reads and discards the rest of an overflowing stream. True when it reached its end, false on a read failure.</summary>
    private static async Task<bool> DrainAsync(Stream stream, byte[] block)
    {
        try
        {
            while (await stream.ReadAsync(block) > 0)
            {
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record StreamRead(byte[] Bytes, bool Ended);
}
