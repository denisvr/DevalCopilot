using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Local-commit's sole interactive child. It owns precisely one <c>update-ref --no-deref --stdin</c> process and never exposes an
/// interactive abstraction outside Infrastructure. The protocol is the exact LF/ASCII form: stdin is written as raw bytes through
/// the actual pipe and closed (a real EOF) after the terminal command, stdout is read as bytes under hard line and total limits,
/// and stderr is drained and counted against its own bound. One transaction deadline covers launch, every protocol exchange and the
/// caller's prepared-lock proof reads; each exchange also has its own phase budget; abort, close, termination and reaping run under
/// an independent cleanup budget that neither the deadline nor a caller token can shorten. Only the child this instance started
/// (and its descendants) is ever terminated. A missing, malformed or oversized acknowledgement, EOF, deadline, cancellation or
/// failed exit leaves a sent <c>commit</c> uncertain; commands are never resent and lock files are never touched by path.
/// </summary>
internal sealed class LocalCommitRefTransaction : IAsyncDisposable
{
    private const int MaxLineBytes = 128;
    private const int MaxStdoutBytes = 1024;
    private const int MaxStderrBytes = 4096;
    private const int MaxCommandBytes = 256;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Process process;
    private readonly LocalCommitRefTransactionBudgets budgets;
    private readonly Action<string>? observer;
    private readonly CancellationTokenSource deadline;
    private readonly CancellationTokenSource drain = new();
    private readonly Stream input;
    private readonly LocalCommitRefProtocolReader output;
    private readonly Task<bool> standardErrorDrain;
    private long standardErrorBytes;
    private volatile bool standardErrorOverflow;
    private bool prepared;
    private bool finished;
    private bool killed;
    private bool disposed;
    private Task<string?>? shutdown;

    private LocalCommitRefTransaction(
        Process process, LocalCommitRefTransactionBudgets budgets, CancellationTokenSource deadline, Action<string>? observer)
    {
        this.process = process;
        this.budgets = budgets;
        this.deadline = deadline;
        this.observer = observer;
        input = process.StandardInput.BaseStream;
        output = new LocalCommitRefProtocolReader(process.StandardOutput.BaseStream, MaxLineBytes, MaxStdoutBytes);
        standardErrorDrain = DrainStandardErrorAsync(process.StandardError.BaseStream, drain.Token);
    }

    /// <summary>The phase and fault of the most recent failure, such as <c>commit:ack_timeout</c>; <c>none</c> otherwise.</summary>
    internal string FailureReason { get; private set; } = "none";

    /// <summary>True when the transaction deadline has passed; no further <c>commit</c> may be sent.</summary>
    internal bool DeadlineExpired => deadline.IsCancellationRequested;

    internal static ProcessStartInfo CreateGitStartInfo(
        string gitPath, string workspacePath, string administrativeDirectory, string emptyConfigPath, string hooksDirectory)
    {
        var info = new ProcessStartInfo(gitPath) { WorkingDirectory = workspacePath };
        foreach (var argument in new[]
        {
            "--no-pager", "--literal-pathspecs", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false",
            "-c", "protocol.allow=never", "-c", "core.attributesFile=" + Forward(emptyConfigPath),
            "-c", "core.hooksPath=" + Forward(hooksDirectory), "-c", "commit.gpgsign=false", "-c", "tag.gpgsign=false",
            "-c", "i18n.commitEncoding=utf-8", "-c", "gc.auto=0", "-c", "core.quotePath=false",
            "--git-dir=" + Forward(administrativeDirectory), "--work-tree=" + Forward(workspacePath),
            "update-ref", "--no-deref", "--stdin",
        })
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment.Clear();
        Copy("SystemRoot", info);
        Copy("ComSpec", info);
        Copy("PATH", info);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = emptyConfigPath;
        info.Environment["GIT_ATTR_NOSYSTEM"] = "1";
        info.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        info.Environment["GIT_NO_LAZY_FETCH"] = "1";
        return info;
    }

    /// <summary>Launches the child and drives <c>start</c>, one <c>update</c> and <c>prepare</c>. On any failure the child has
    /// already been aborted, closed, terminated if necessary and reaped; the result says whether the absence of mutation is proven.</summary>
    internal static async Task<LocalCommitRefTransactionStart> StartAsync(
        ProcessStartInfo info,
        string branchName,
        string commitSha,
        string parentSha,
        LocalCommitRefTransactionBudgets budgets,
        Action<string>? observer,
        CancellationToken caller)
    {
        if (branchName.Length is 0 or > 200 || !IsRefComponent(branchName) || !IsObjectId(commitSha) || !IsObjectId(parentSha))
        {
            return new LocalCommitRefTransactionStart(null, "launch:invalid_arguments", true);
        }

        if (caller.IsCancellationRequested)
        {
            return new LocalCommitRefTransactionStart(null, "launch:cancelled", true);
        }

        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardInputEncoding = Utf8;
        info.StandardOutputEncoding = Utf8;
        info.StandardErrorEncoding = Utf8;

        var deadline = new CancellationTokenSource(budgets.Total);
        Process? process = null;
        try
        {
            process = new Process { StartInfo = info };
            process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException
            or NotSupportedException or PlatformNotSupportedException)
        {
            process?.Dispose();
            deadline.Dispose();
            return new LocalCommitRefTransactionStart(null, "launch:failed", true);
        }

        var owner = new LocalCommitRefTransaction(process, budgets, deadline, observer);
        string? reason;
        try
        {
            owner.Notify("launched");
            reason = await owner.RunStartAsync(branchName, commitSha, parentSha, caller);
        }
        catch (Exception)
        {
            reason = "start:exception";
        }

        if (reason is null)
        {
            owner.prepared = true;
            owner.Notify("prepared");
            return new LocalCommitRefTransactionStart(owner, "none", false);
        }

        // Commit was never sent: Git can only have released its own locks if the child ended without being terminated.
        owner.finished = true;
        await owner.ShutdownAsync("abort", "abort: ok", "abort", sendCommand: true);
        var noMutationProven = !owner.killed;
        await owner.DisposeAsync();
        return new LocalCommitRefTransactionStart(null, reason, noMutationProven);
    }

    /// <summary>Runs the caller's read-only proof under one disposed scope bound to the owner's overall deadline and the caller's
    /// token. False means the proof failed or the deadline passed (see <see cref="FailureReason"/>); a caller cancellation is
    /// rethrown so the caller can abort and propagate.</summary>
    internal async Task<bool> RunProofAsync(Func<CancellationToken, Task<bool>> proof, CancellationToken caller)
    {
        if (!prepared || finished || disposed)
        {
            FailureReason = "proof:not_prepared";
            return false;
        }

        Notify("proof_started");
        using var scope = LinkScope(caller);
        Task<bool>? running = null;
        bool passed;
        try
        {
            running = proof(scope.Token);
            passed = await running.WaitAsync(scope.Token);
        }
        catch (OperationCanceledException)
        {
            // The abandoned proof is cancelled by the same scope; observe its eventual fault so nothing is left unobserved.
            running?.ContinueWith(
                static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            if (caller.IsCancellationRequested)
            {
                throw;
            }

            FailureReason = "proof:deadline";
            Notify("proof_failed");
            return false;
        }

        if (!passed)
        {
            FailureReason = "proof:failed";
            Notify("proof_failed");
            return false;
        }

        if (deadline.IsCancellationRequested)
        {
            FailureReason = "proof:deadline";
            Notify("proof_failed");
            return false;
        }

        Notify("proof_passed");
        return true;
    }

    /// <summary>Sends <c>commit</c> exactly once, only before the deadline and caller cancellation. Everything after the send is
    /// uncertain unless the exact acknowledgement, a real EOF, a clean zero exit and bounded streams all hold.</summary>
    internal async Task<LocalCommitRefCommitOutcome> CommitAsync(CancellationToken caller)
    {
        if (!prepared || finished || disposed)
        {
            FailureReason = "commit:not_prepared";
            return LocalCommitRefCommitOutcome.NotAttempted;
        }

        if (deadline.IsCancellationRequested || caller.IsCancellationRequested)
        {
            FailureReason = caller.IsCancellationRequested ? "commit:cancelled_before_send" : "commit:deadline_before_send";
            return LocalCommitRefCommitOutcome.NotAttempted;
        }

        finished = true;
        string? reason;
        try
        {
            using var scope = LinkScope(caller, budgets.Phase);
            reason = await ExchangeAsync("commit", "commit", "commit: ok", "commit_sent", "commit_acknowledged", scope.Token, caller, false);
        }
        catch (Exception)
        {
            reason = "commit:exception";
        }

        var shutdownReason = await ShutdownAsync(null, null, "commit", sendCommand: false);
        reason ??= shutdownReason;
        if (reason is not null)
        {
            FailureReason = reason;
            return LocalCommitRefCommitOutcome.Uncertain;
        }

        Notify("committed");
        return LocalCommitRefCommitOutcome.Acknowledged;
    }

    /// <summary>Sends <c>abort</c> under the cleanup budget, closes stdin, and terminates and reaps the owned child if it does not
    /// exit by itself. Confirmed only for the exact acknowledgement plus a clean, un-terminated zero exit.</summary>
    internal async Task<LocalCommitRefAbortOutcome> AbortAsync()
    {
        if (finished || disposed)
        {
            FailureReason = "abort:not_attempted";
            return LocalCommitRefAbortOutcome.Unconfirmed;
        }

        finished = true;
        var reason = await ShutdownAsync("abort", "abort: ok", "abort", sendCommand: true);
        if (reason is not null || killed)
        {
            FailureReason = reason ?? "abort:killed";
            return LocalCommitRefAbortOutcome.Unconfirmed;
        }

        Notify("aborted");
        return LocalCommitRefAbortOutcome.Confirmed;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        if (!finished)
        {
            finished = true;
            await ShutdownAsync("abort", "abort: ok", "abort", sendCommand: true);
        }
        else
        {
            await ShutdownAsync(null, null, "close", sendCommand: false);
        }

        disposed = true;
        drain.Dispose();
        deadline.Dispose();
        process.Dispose();
    }

    private async Task<string?> RunStartAsync(string branchName, string commitSha, string parentSha, CancellationToken caller)
    {
        using (var scope = LinkScope(caller, budgets.Phase))
        {
            var reason = await ExchangeAsync("start", "start", "start: ok", "start_sent", "start_acknowledged", scope.Token, caller, false);
            if (reason is not null)
            {
                return reason;
            }
        }

        using (var scope = LinkScope(caller, budgets.Phase))
        {
            var command = $"update refs/heads/{branchName} {commitSha} {parentSha}";
            var reason = await ExchangeAsync("update", command, null, "update_sent", null, scope.Token, caller, false);
            if (reason is not null)
            {
                return reason;
            }
        }

        using (var scope = LinkScope(caller, budgets.Phase))
        {
            return await ExchangeAsync("prepare", "prepare", "prepare: ok", "prepare_sent", null, scope.Token, caller, false);
        }
    }

    /// <summary>One protocol exchange: write an exact LF-terminated command through the actual pipe, then (when an
    /// acknowledgement is expected) read exactly one admitted line. Returns null on success, otherwise <c>phase:fault</c>.</summary>
    private async Task<string?> ExchangeAsync(
        string phase,
        string command,
        string? acknowledgement,
        string sentEvent,
        string? acknowledgedEvent,
        CancellationToken token,
        CancellationToken caller,
        bool cleanup)
    {
        var send = await TrySendAsync(command, token);
        if (send is not null)
        {
            return phase + ":send_" + (send == "cancelled" ? CancelKind(caller, cleanup) : send);
        }

        Notify(sentEvent);
        if (acknowledgement is null)
        {
            return StandardErrorFault(phase);
        }

        var line = await output.ReadLineAsync(token);
        var fault = line.Kind switch
        {
            LocalCommitRefProtocolReader.Kind.Line => string.Equals(line.Text, acknowledgement, StringComparison.Ordinal)
                ? null
                : "ack_unexpected",
            LocalCommitRefProtocolReader.Kind.Eof => "ack_eof",
            LocalCommitRefProtocolReader.Kind.Overflow => "ack_overflow",
            LocalCommitRefProtocolReader.Kind.Malformed => "ack_malformed",
            _ => "ack_" + CancelKind(caller, cleanup),
        };
        if (fault is not null)
        {
            return phase + ":" + fault;
        }

        if (output.HasPendingBytes)
        {
            return phase + ":extra_output";
        }

        if (StandardErrorFault(phase) is { } overflow)
        {
            return overflow;
        }

        if (acknowledgedEvent is not null)
        {
            Notify(acknowledgedEvent);
        }

        return null;
    }

    private string? StandardErrorFault(string phase) => standardErrorOverflow ? phase + ":stderr_overflow" : null;

    /// <summary>Writes an exact printable-ASCII command plus one LF as raw bytes. Returns null when written and flushed, otherwise a
    /// fault word. A fault after any byte may have been delivered is never retried.</summary>
    private async Task<string?> TrySendAsync(string command, CancellationToken token)
    {
        if (command.Length is 0 or > MaxCommandBytes || command.Any(character => character is < ' ' or > '~'))
        {
            return "failed";
        }

        var bytes = Encoding.ASCII.GetBytes(command + "\n");
        try
        {
            await input.WriteAsync(bytes.AsMemory(), token).AsTask().WaitAsync(token);
            await input.FlushAsync(token).WaitAsync(token);
            return null;
        }
        catch (OperationCanceledException)
        {
            return "cancelled";
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return "failed";
        }
    }

    /// <summary>Optionally sends the terminal abort, then performs the actual EOF, bounded exit wait, owned-child termination,
    /// reaping, stream drain and trailing-output checks, all inside the cleanup budget. Runs at most once; returns the first fault.</summary>
    private Task<string?> ShutdownAsync(string? command, string? acknowledgement, string phase, bool sendCommand) =>
        shutdown ??= ShutdownCoreAsync(command, acknowledgement, phase, sendCommand);

    private async Task<string?> ShutdownCoreAsync(string? command, string? acknowledgement, string phase, bool sendCommand)
    {
        using var overall = new CancellationTokenSource(budgets.Cleanup);
        // Within one hard cap: the abort exchange may use the first two fifths, the exit wait up to three quarters, and the
        // remainder is reserved for reaping a terminated child. A silent child therefore cannot consume the exit wait.
        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
        exchange.CancelAfter(TimeSpan.FromTicks(budgets.Cleanup.Ticks * 2 / 5));
        using var graceful = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
        graceful.CancelAfter(TimeSpan.FromTicks(budgets.Cleanup.Ticks * 3 / 4));

        string? reason = null;
        if (sendCommand && command is not null && acknowledgement is not null && !HasExited())
        {
            try
            {
                reason = await ExchangeAsync(
                    phase, command, acknowledgement, phase + "_sent", phase + "_acknowledged", exchange.Token, default, true);
            }
            catch (Exception)
            {
                reason = phase + ":exception";
            }
        }

        // This is EOF on the actual child pipe, not disposal of a leave-open wrapper.
        TryCloseInput();
        var exited = await WaitForExitAsync(graceful.Token);
        if (!exited)
        {
            reason ??= phase + ":exit_timeout";
            TerminateOwnedChild();
            exited = await WaitForExitAsync(overall.Token);
            if (!exited)
            {
                reason = phase + ":reap_timeout";
            }
        }

        if (exited)
        {
            // Success needs explicit end-of-file evidence on both pipes: a drain that faulted or was cancelled, and a final stdout
            // read that failed or was cancelled, prove nothing about unsolicited output and can never stand for a clean close.
            switch (await DrainOutcomeAsync(overall.Token))
            {
                case DrainOutcome.Unfinished:
                    reason ??= phase + ":stderr_unfinished";
                    break;
                case DrainOutcome.Unconfirmed:
                    reason ??= phase + ":stderr_unconfirmed";
                    break;
            }

            if (standardErrorOverflow)
            {
                reason ??= phase + ":stderr_overflow";
            }

            var trailing = await output.HasTrailingBytesAsync(overall.Token);
            if (trailing == true)
            {
                reason ??= phase + ":extra_output";
            }
            else if (trailing is null)
            {
                reason ??= phase + ":stdout_unconfirmed";
            }

            if (process.HasExited && process.ExitCode != 0)
            {
                reason ??= phase + ":exit_code";
            }
        }

        drain.Cancel();
        return reason;
    }

    private enum DrainOutcome
    {
        /// <summary>Standard error reached a real end of file.</summary>
        Eof,

        /// <summary>The drain did not finish inside the cleanup budget.</summary>
        Unfinished,

        /// <summary>The drain finished, but only because a read faulted or was cancelled.</summary>
        Unconfirmed,
    }

    private async Task<DrainOutcome> DrainOutcomeAsync(CancellationToken token)
    {
        try
        {
            return await standardErrorDrain.WaitAsync(token) ? DrainOutcome.Eof : DrainOutcome.Unconfirmed;
        }
        catch (OperationCanceledException)
        {
            drain.Cancel();
            return DrainOutcome.Unfinished;
        }
    }

    /// <summary>Drains standard error within its byte bound. True only when a read actually returned end of file; a faulted,
    /// disposed or cancelled read ends the drain with false, which is never confirmation.</summary>
    private async Task<bool> DrainStandardErrorAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[512];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), token).AsTask().WaitAsync(token);
                if (read == 0)
                {
                    return true;
                }

                standardErrorBytes += read;
                if (standardErrorBytes > MaxStderrBytes)
                {
                    standardErrorOverflow = true;
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException
            or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<bool> WaitForExitAsync(CancellationToken token)
    {
        try
        {
            await process.WaitForExitAsync(token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private bool HasExited()
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private void TerminateOwnedChild()
    {
        killed = true;
        Notify("killed");
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException
            or AggregateException)
        {
        }
    }

    private void TryCloseInput()
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ObjectDisposedException)
        {
        }
    }

    private CancellationTokenSource LinkScope(CancellationToken caller, TimeSpan? phaseBudget = null)
    {
        var scope = caller.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, caller)
            : CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        if (phaseBudget is { } budget)
        {
            scope.CancelAfter(budget);
        }

        return scope;
    }

    private string CancelKind(CancellationToken caller, bool cleanup) =>
        cleanup ? "timeout" : caller.IsCancellationRequested ? "cancelled" : deadline.IsCancellationRequested ? "deadline" : "timeout";

    private void Notify(string phase) => observer?.Invoke(phase);

    private static bool IsObjectId(string value) => value.Length == 40 && value.All(char.IsAsciiHexDigit);

    private static bool IsRefComponent(string value) =>
        value.All(character => character is > ' ' and < '\u007f' && character is not ('\\' or '~' or '^' or ':' or '?' or '*' or '['));

    private static string Forward(string value) => value.Replace('\\', '/');

    private static void Copy(string name, ProcessStartInfo info)
    {
        if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
        {
            info.Environment[name] = value;
        }
    }
}
