using System.Text;
using DevalCopilot.Domain.Features.Processes;

namespace DevalCopilot.Infrastructure.Features.Processes;

/// <summary>
/// Captures one process stream up to its own cap and the cross-stream shared budget. Always
/// reads the source stream through to its natural end regardless of whether bytes are still
/// being stored: once the cap is reached, excess bytes are discarded rather than kept, but the
/// read loop keeps draining so the child process never blocks writing to a pipe nobody is
/// reading and can still exit normally (or be killed and reach end-of-stream) on its own.
///
/// Every accepted byte passes through a <see cref="StreamingOutputRedactor"/> before it reaches
/// either the in-memory buffer or the optional durable sink file — raw process output exists
/// only transiently, inside the read loop below, never in <see cref="Text"/> or on disk.
///
/// When a sink path is supplied, this type owns that file's only write handle: the child process
/// never has it open at all (the child only ever owns its own stdout/stderr pipe, which this
/// type reads from).
///
/// ADR-0072: a close request freezes the returned output at once, independently of any writer: under a short buffer lock that is
/// never held across I/O it takes an immutable snapshot of the buffer and the truncation fact, so nothing appended later can reach
/// <see cref="Text"/> or <see cref="Truncated"/>, and every later chunk is dropped before it reaches the buffer or the sink. The same
/// request cancels the writer's token, which reaches an in-flight sink write or flush (the sink is opened for asynchronous,
/// unbuffered I/O, so the cancellation is real). <see cref="CloseAsync"/> then proves the writer closed only by acquiring the write
/// gate AND finishing the sink's disposal within its budget; otherwise it reports the writer unproved and the stream is
/// <see cref="OutputCaptureCompletion.Unknown"/>, so its partial file must not be sealed. A writer that was holding the gate finishes
/// the closure itself once its I/O returns; that late closure never changes a completion already reported. A stream that never
/// reaches end-of-file never releases the redactor's held-back tail, which may hold the start of a secret.
/// </summary>
internal sealed class BoundedOutputCapture : IAsyncDisposable
{
    private readonly int _streamCap;
    private readonly SharedCaptureBudget _sharedBudget;
    private readonly StreamingOutputRedactor _redactor = new();
    private readonly MemoryStream _buffer = new();
    private readonly object _bufferLock = new();
    private readonly Stream? _sink;
    private readonly SemaphoreSlim _writerGate = new(1, 1);
    private readonly CancellationTokenSource _writerCancellation = new();
    private readonly object _disposalLock = new();
    private Task? _sinkDisposal;
    private bool _truncated;
    private bool _closeRequested;
    private byte[]? _closedText;
    private bool _closedTruncated;
    private volatile bool _writerClosed;
    private volatile bool _deadlineExceeded;
    private volatile bool _reachedEndOfFile;
    private volatile bool _faulted;

    public BoundedOutputCapture(int streamCap, SharedCaptureBudget sharedBudget, string? sinkPath = null)
        : this(streamCap, sharedBudget, sinkPath is null ? null : OpenSink(sinkPath))
    {
    }

    /// <summary>Uses <paramref name="sink"/> as the durable writer; the capture owns and disposes it.</summary>
    internal BoundedOutputCapture(int streamCap, SharedCaptureBudget sharedBudget, Stream? sink)
    {
        _streamCap = streamCap;
        _sharedBudget = sharedBudget;
        _sink = sink;
    }

    /// <summary>
    /// The production sink: FileShare.Read only (no other writer is ever legitimate, and a concurrent live reader opens its own
    /// handle, see FilesystemArtifactStore), no managed buffer (nothing is left for disposal to write) and asynchronous I/O, so a
    /// cancelled write or flush is cancelled in the operating system rather than only before it starts.
    /// </summary>
    internal static Stream OpenSink(string sinkPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(sinkPath)!);
        return new FileStream(sinkPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 0, FileOptions.Asynchronous);
    }

    /// <summary>Reads <paramref name="source"/> to end-of-file or until <paramref name="cancellationToken"/> is cancelled. Never
    /// throws: a read failure, a sink failure or a cancellation is recorded for <see cref="GetCompletion"/>.</summary>
    public async Task DrainAsync(Stream source, CancellationToken cancellationToken)
    {
        var readBuffer = new byte[8192];
        try
        {
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (!await AcceptAsync(_redactor.ProcessChunk(readBuffer.AsSpan(0, bytesRead))).ConfigureAwait(false))
                {
                    return;
                }
            }

            if (await AcceptAsync(_redactor.Flush(), finalChunk: true).ConfigureAwait(false))
            {
                _reachedEndOfFile = true;
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException
                                              or NotSupportedException or InvalidOperationException)
        {
            // After the completion window expired the host cancels the read and closes its end of the pipe, so a cancellation or
            // a disposed-stream failure from then on is that deadline, not an independent fault.
            if (!_deadlineExceeded && !IsCloseRequested)
            {
                _faulted = true;
            }
        }
    }

    /// <summary>Records that the bounded I/O completion window expired before this stream ended.</summary>
    public void MarkDeadlineExceeded()
    {
        if (!_reachedEndOfFile)
        {
            _deadlineExceeded = true;
        }
    }

    /// <summary>
    /// Freezes the returned output, cancels the writer's I/O and closes the writer under the write gate, spending at most
    /// <paramref name="budget"/> on the gate and the sink's disposal together. Returns true only when the writer is demonstrably
    /// closed; false leaves it unproved (nothing more can reach the returned output either way). Idempotent.
    /// </summary>
    public async Task<bool> CloseAsync(TimeSpan budget)
    {
        RequestClose();
        if (_writerClosed)
        {
            return true;
        }

        var deadline = DateTimeOffset.UtcNow + (budget < TimeSpan.Zero ? TimeSpan.Zero : budget);
        if (!await _writerGate.WaitAsync(Remaining(deadline)).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            return await FinishCloseAsync(Remaining(deadline)).ConfigureAwait(false);
        }
        finally
        {
            _writerGate.Release();
        }
    }

    /// <summary>How the stream ended, given whether <see cref="CloseAsync"/> proved the writer closed. A writer that could not
    /// be proved closed is always <see cref="OutputCaptureCompletion.Unknown"/>.</summary>
    public OutputCaptureCompletion GetCompletion(bool writerClosed)
    {
        if (!writerClosed)
        {
            return OutputCaptureCompletion.Unknown;
        }

        if (_faulted)
        {
            return OutputCaptureCompletion.Faulted;
        }

        if (_reachedEndOfFile)
        {
            return OutputCaptureCompletion.Complete;
        }

        return _deadlineExceeded ? OutputCaptureCompletion.DeadlineExceeded : OutputCaptureCompletion.Unknown;
    }

    /// <summary>The redacted output. Once a close was requested it is the immutable snapshot taken at that instant, whatever any
    /// writer still does.</summary>
    public string Text
    {
        get
        {
            lock (_bufferLock)
            {
                return Encoding.UTF8.GetString(_closedText ?? _buffer.ToArray());
            }
        }
    }

    public bool Truncated
    {
        get
        {
            lock (_bufferLock)
            {
                return _closeRequested ? _closedTruncated : _truncated;
            }
        }
    }

    /// <summary>Requests the closure without waiting: the output is frozen and the writer's I/O cancelled at once, and the sink is
    /// disposed now only if no write holds the gate (otherwise that writer disposes it when its I/O returns).</summary>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync(TimeSpan.Zero).ConfigureAwait(false);
    }

    private bool IsCloseRequested
    {
        get
        {
            lock (_bufferLock)
            {
                return _closeRequested;
            }
        }
    }

    private void RequestClose()
    {
        lock (_bufferLock)
        {
            if (!_closeRequested)
            {
                _closeRequested = true;
                _closedText = _buffer.ToArray();
                _closedTruncated = _truncated;
            }
        }

        _writerCancellation.Cancel();
    }

    private async Task<bool> AcceptAsync(byte[] redactedChunk, bool finalChunk = false)
    {
        await _writerGate.WaitAsync().ConfigureAwait(false);
        try
        {
            int granted;
            lock (_bufferLock)
            {
                if (_closeRequested)
                {
                    // A chunk read after the closure was requested: dropped, never appended to the buffer or the sink.
                    return false;
                }

                var streamRemaining = Math.Max(_streamCap - (int)_buffer.Length, 0);
                var requested = Math.Min(redactedChunk.Length, streamRemaining);
                granted = requested > 0 ? _sharedBudget.Reserve(requested) : 0;
                if (granted > 0)
                {
                    _buffer.Write(redactedChunk, 0, granted);
                }

                if (granted < redactedChunk.Length)
                {
                    _truncated = true;
                }
            }

            if (granted > 0)
            {
                await WriteSinkAsync(redactedChunk.AsMemory(0, granted)).ConfigureAwait(false);
            }

            if (finalChunk)
            {
                await FlushSinkAsync().ConfigureAwait(false);
            }

            // Only a chunk whose I/O finished before any close request counts; the end of file is recorded on that basis alone.
            return !IsCloseRequested;
        }
        finally
        {
            if (IsCloseRequested && !_writerClosed)
            {
                // The closure was requested while this write held the gate: finish it now that the write returned. This late
                // closure changes no completion already reported.
                await FinishCloseAsync(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            }

            _writerGate.Release();
        }
    }

    /// <summary>Runs under the write gate. Disposes the sink once (a disposal still pending is awaited, never started again) and
    /// proves the writer closed only when that disposal finished within <paramref name="budget"/>.</summary>
    private async Task<bool> FinishCloseAsync(TimeSpan budget)
    {
        if (_writerClosed)
        {
            return true;
        }

        if (_sink is null)
        {
            _writerClosed = true;
            return true;
        }

        Task disposal;
        lock (_disposalLock)
        {
            disposal = _sinkDisposal ??= DisposeSinkAsync();
        }

        if (!disposal.IsCompleted)
        {
            if (budget == Timeout.InfiniteTimeSpan)
            {
                await disposal.ConfigureAwait(false);
            }
            else if (await Task.WhenAny(disposal, Task.Delay(budget)).ConfigureAwait(false) != disposal)
            {
                return false;
            }
        }

        _writerClosed = true;
        return true;
    }

    private async Task DisposeSinkAsync()
    {
        try
        {
            await _sink!.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // The handle is gone either way; what was written may be incomplete.
            _faulted = true;
        }
    }

    private async Task WriteSinkAsync(ReadOnlyMemory<byte> bytes)
    {
        if (_sink is null || _faulted)
        {
            return;
        }

        try
        {
            // Flushed immediately, not just once at the end of DrainAsync: a live reader (the cockpit's output poll, reading
            // through its own independently opened handle) must be able to observe accepted bytes promptly.
            await _sink.WriteAsync(bytes, _writerCancellation.Token).ConfigureAwait(false);
            await _sink.FlushAsync(_writerCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_writerCancellation.IsCancellationRequested)
        {
            // Cancelled by a close request: the stream is incomplete (it cannot be Complete), not independently faulted.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // The pipe keeps draining so the child never blocks; the stream is recorded Faulted instead of complete.
            _faulted = true;
        }
    }

    private async Task FlushSinkAsync()
    {
        if (_sink is null || _faulted)
        {
            return;
        }

        try
        {
            await _sink.FlushAsync(_writerCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_writerCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            _faulted = true;
        }
    }

    private static TimeSpan Remaining(DateTimeOffset deadline)
    {
        var remaining = deadline - DateTimeOffset.UtcNow;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}
