using System.Diagnostics;
using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Processes;
using DevalCopilot.Infrastructure.Features.Processes;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Processes;

/// <summary>
/// ADR-0072 (review WEP-R1): the capture's finalization lifecycle when the durable sink does not cooperate. A close request freezes
/// the returned output, cancels the writer's I/O and proves the writer closed only within its budget; a held write, a held flush
/// and a delayed disposal each leave the writer unproved (Unknown) instead of being reported closed, no chunk read after the close
/// request reaches the buffer or the sink, and the held writer finalizes the sink itself once its I/O returns. The production
/// adapter is driven the same way with a real owned fixture process. No provider executable is involved.
/// </summary>
public sealed class BoundedCaptureFinalizationTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");

    /// <summary>The five-second completion window, the one-second release window and a scheduling margin.</summary>
    private static readonly TimeSpan BoundedFinalization = TimeSpan.FromSeconds(12);

    /// <summary>The redactor's held-back tail of a stream that has not reached its end (the longest pattern minus one).</summary>
    private const int CarryOver = 127;

    private readonly string _approvedRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-capture-finalization-{Guid.NewGuid():N}");

    public BoundedCaptureFinalizationTests() => Directory.CreateDirectory(_approvedRoot);

    [Fact]
    public async Task A_held_sink_write_that_ignores_cancellation_leaves_the_writer_unproved_and_freezes_the_returned_output()
    {
        var sink = new ControlledSink { HoldWrites = true };
        var source = new ControlledSource();
        var capture = new BoundedOutputCapture(1024, new SharedCaptureBudget(1024), sink);
        var drain = capture.DrainAsync(source, CancellationToken.None);

        source.Release(Bytes('A', 300));
        await sink.WriteEntered.WaitAsync(TimeSpan.FromSeconds(5));
        capture.MarkDeadlineExceeded();

        var clock = Stopwatch.StartNew();
        Assert.False(await capture.CloseAsync(TimeSpan.FromMilliseconds(200)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), "the close is bounded by its budget");
        var text = capture.Text;
        var writeCallsAtClose = sink.WriteCalls;

        // The redactor still withholds its carry-over of this unended stream, so the accepted text is the released prefix.
        Assert.Equal(new string('A', 300 - CarryOver), text);
        Assert.False(capture.Truncated);

        // The disposal request returns at once and does not wait for the held writer either.
        clock.Restart();
        await capture.DisposeAsync();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), "disposal does not wait for the held write");

        // More output arrives while the write is held, and would truncate the 1 KiB stream if it were accepted.
        source.Release(Bytes('B', 900));
        source.Complete();
        Assert.Equal(text, capture.Text);
        Assert.False(capture.Truncated);

        sink.ReleaseHeldWrites();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        await sink.Disposed.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(text, capture.Text);
        Assert.False(capture.Truncated);
        Assert.Equal(writeCallsAtClose, sink.WriteCalls);
        Assert.DoesNotContain((byte)'B', sink.Bytes);
        Assert.Equal(1, sink.DisposeCalls);
        Assert.Equal(OutputCaptureCompletion.Unknown, capture.GetCompletion(writerClosed: false));
    }

    [Fact]
    public async Task A_held_sink_write_that_honors_cancellation_is_cancelled_by_the_close_and_the_writer_is_proved_closed()
    {
        var sink = new ControlledSink { HoldWrites = true, HonorCancellation = true };
        var source = new ControlledSource();
        var capture = new BoundedOutputCapture(1024, new SharedCaptureBudget(1024), sink);
        var drain = capture.DrainAsync(source, CancellationToken.None);

        source.Release(Bytes('A', 300));
        await sink.WriteEntered.WaitAsync(TimeSpan.FromSeconds(5));
        capture.MarkDeadlineExceeded();

        Assert.True(await capture.CloseAsync(TimeSpan.FromSeconds(1)));
        Assert.True(sink.WriteCancelled);
        Assert.Equal(1, sink.DisposeCalls);

        source.Release(Bytes('B', 300));
        source.Complete();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(sink.Bytes);
        Assert.Equal(new string('A', 300 - CarryOver), capture.Text);
        Assert.Equal(OutputCaptureCompletion.DeadlineExceeded, capture.GetCompletion(writerClosed: true));
    }

    [Fact]
    public async Task A_held_flush_leaves_the_writer_unproved_and_the_stream_never_complete()
    {
        var sink = new ControlledSink { HoldFlushes = true };
        var source = new ControlledSource();
        var capture = new BoundedOutputCapture(1024, new SharedCaptureBudget(1024), sink);
        var drain = capture.DrainAsync(source, CancellationToken.None);

        source.Release(Bytes('A', 1000));
        await sink.FlushEntered.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(await capture.CloseAsync(TimeSpan.FromMilliseconds(200)));
        var text = capture.Text;

        source.Complete();
        sink.ReleaseHeldFlushes();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        await sink.Disposed.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(text, capture.Text);
        Assert.Equal(OutputCaptureCompletion.Unknown, capture.GetCompletion(writerClosed: false));
        // Even a caller that wrongly claimed the writer closed could not obtain Complete: the end of file came after the request.
        Assert.NotEqual(OutputCaptureCompletion.Complete, capture.GetCompletion(writerClosed: true));
    }

    [Fact]
    public async Task A_delayed_sink_disposal_is_bounded_reported_unproved_and_never_started_twice()
    {
        var sink = new ControlledSink { DisposalDelay = TimeSpan.FromSeconds(2) };
        var source = new ControlledSource();
        var capture = new BoundedOutputCapture(1024, new SharedCaptureBudget(1024), sink);
        var drain = capture.DrainAsync(source, CancellationToken.None);
        source.Release(Bytes('A', 64));
        source.Complete();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));

        var clock = Stopwatch.StartNew();
        Assert.False(await capture.CloseAsync(TimeSpan.FromMilliseconds(200)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), "the disposal wait is bounded by the close budget");
        Assert.Equal(OutputCaptureCompletion.Unknown, capture.GetCompletion(writerClosed: false));

        Assert.True(await capture.CloseAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, sink.DisposeCalls);
        Assert.Equal(OutputCaptureCompletion.Complete, capture.GetCompletion(writerClosed: true));
        Assert.Equal(new string('A', 64), capture.Text);
    }

    [Fact]
    public async Task A_chunk_read_after_an_unproved_close_while_the_disposal_is_pending_never_reaches_the_buffer_or_the_sink()
    {
        var sink = new ControlledSink { DisposalDelay = TimeSpan.FromSeconds(2) };
        var source = new ControlledSource();
        var capture = new BoundedOutputCapture(4096, new SharedCaptureBudget(4096), sink);
        var drain = capture.DrainAsync(source, CancellationToken.None);
        source.Release(Bytes('A', 600));
        await WaitUntilAsync(() => sink.WriteCalls == 1);

        Assert.False(await capture.CloseAsync(TimeSpan.FromMilliseconds(200)));
        var text = capture.Text;

        // The gate is free again while the disposal is still pending: the next read must still be dropped.
        source.Release(Bytes('B', 600));
        await drain.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, sink.WriteCalls);
        Assert.DoesNotContain((byte)'B', sink.Bytes);
        Assert.Equal(text, capture.Text);
        Assert.False(capture.Truncated);
        Assert.True(await capture.CloseAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, sink.DisposeCalls);
    }

    [Fact]
    public void The_production_sink_is_opened_for_asynchronous_cancellable_unbuffered_io()
    {
        using var sink = BoundedOutputCapture.OpenSink(Path.Combine(_approvedRoot, "sink", "stdout.partial"));

        var file = Assert.IsType<FileStream>(sink);
        Assert.True(file.IsAsync);
        Assert.True(file.CanWrite);
    }

    [Fact]
    public async Task The_adapter_returns_within_its_bounds_with_an_unproved_writer_and_immutable_output_while_a_sink_write_is_held()
    {
        var sinks = new Dictionary<string, ControlledSink>();
        var adapter = new ChildProcessExecutionAdapter(path =>
        {
            var sink = new ControlledSink { HoldWrites = path.EndsWith("stdout.partial", StringComparison.Ordinal) };
            sinks[path] = sink;
            return sink;
        });

        var clock = Stopwatch.StartNew();
        var result = await adapter.ExecuteAsync(CreateRequest(["write-bytes", "4000", "7"]), CancellationToken.None);
        clock.Stop();

        var output = sinks.Single(pair => pair.Key.EndsWith("stdout.partial", StringComparison.Ordinal)).Value;
        var error = sinks.Single(pair => pair.Key.EndsWith("stderr.partial", StringComparison.Ordinal)).Value;
        Assert.True(clock.Elapsed < BoundedFinalization, $"returned after {clock.Elapsed}");
        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Termination);
        Assert.Equal(OutputCaptureCompletion.Unknown, result.IoCompletion.StandardOutput);
        Assert.Equal(OutputCaptureCompletion.Complete, result.IoCompletion.StandardError);
        Assert.False(result.IoCompletion.IsComplete);
        Assert.False(result.StandardOutputTruncated);
        Assert.Equal(1, error.DisposeCalls);

        var writeCallsAtReturn = output.WriteCalls;
        Assert.Equal(0, output.DisposeCalls);
        output.ReleaseHeldWrites();
        await output.Disposed.WaitAsync(TimeSpan.FromSeconds(5));

        // Only what was accepted before the close request is returned; nothing later reached the output.
        Assert.NotEmpty(result.StandardOutput);
        Assert.True(result.StandardOutput.Length < 4000);
        Assert.Matches("^A+$", result.StandardOutput);
        Assert.Equal(writeCallsAtReturn, output.WriteCalls);
        Assert.Equal(1, output.DisposeCalls);
    }

    [Fact]
    public async Task The_adapter_cancels_a_held_cancellable_sink_write_and_proves_the_writer_closed_within_its_bounds()
    {
        var sinks = new Dictionary<string, ControlledSink>();
        var adapter = new ChildProcessExecutionAdapter(path =>
        {
            var sink = new ControlledSink { HoldWrites = path.EndsWith("stdout.partial", StringComparison.Ordinal), HonorCancellation = true };
            sinks[path] = sink;
            return sink;
        });

        var clock = Stopwatch.StartNew();
        var result = await adapter.ExecuteAsync(CreateRequest(["write-bytes", "4000", "7"]), CancellationToken.None);
        clock.Stop();

        var output = sinks.Single(pair => pair.Key.EndsWith("stdout.partial", StringComparison.Ordinal)).Value;
        Assert.True(clock.Elapsed < BoundedFinalization, $"returned after {clock.Elapsed}");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(OutputCaptureCompletion.DeadlineExceeded, result.IoCompletion.StandardOutput);
        Assert.Equal(OutputCaptureCompletion.Complete, result.IoCompletion.StandardError);
        Assert.True(output.WriteCancelled);
        Assert.Equal(1, output.DisposeCalls);
        Assert.Empty(output.Bytes);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_approvedRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ProcessExecutionRequest CreateRequest(IReadOnlyList<string> arguments) => new()
    {
        ExecutablePath = FixtureExecutablePath,
        Arguments = arguments,
        WorkingDirectory = _approvedRoot,
        ApprovedRoot = _approvedRoot,
        Timeout = TimeSpan.FromSeconds(10),
        StandardOutputSinkPath = Path.Combine(_approvedRoot, "out", "stdout.partial"),
        StandardErrorSinkPath = Path.Combine(_approvedRoot, "out", "stderr.partial"),
    };

    private static byte[] Bytes(char value, int count) => Encoding.ASCII.GetBytes(new string(value, count));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "the condition was not reached");
            await Task.Delay(10);
        }
    }

    /// <summary>A write-only sink whose writes, flushes and disposal the test can hold, with or without honoring cancellation.</summary>
    private sealed class ControlledSink : Stream
    {
        private readonly TaskCompletionSource _writeHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _flushHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _writeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _flushEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly MemoryStream _bytes = new();
        private int _writeCalls;
        private int _disposeCalls;

        public bool HoldWrites { get; init; }

        public bool HoldFlushes { get; init; }

        public bool HonorCancellation { get; init; }

        public TimeSpan DisposalDelay { get; init; }

        public Task WriteEntered => _writeEntered.Task;

        public Task FlushEntered => _flushEntered.Task;

        public Task Disposed => _disposed.Task;

        public int WriteCalls => Volatile.Read(ref _writeCalls);

        public int DisposeCalls => Volatile.Read(ref _disposeCalls);

        public bool WriteCancelled { get; private set; }

        public byte[] Bytes
        {
            get
            {
                lock (_bytes)
                {
                    return _bytes.ToArray();
                }
            }
        }

        public void ReleaseHeldWrites() => _writeHold.TrySetResult();

        public void ReleaseHeldFlushes() => _flushHold.TrySetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _writeCalls);
            if (HoldWrites)
            {
                _writeEntered.TrySetResult();
                try
                {
                    await (HonorCancellation ? _writeHold.Task.WaitAsync(cancellationToken) : _writeHold.Task);
                }
                catch (OperationCanceledException)
                {
                    WriteCancelled = true;
                    throw;
                }
            }

            lock (_bytes)
            {
                _bytes.Write(buffer.Span);
            }
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (HoldFlushes)
            {
                _flushEntered.TrySetResult();
                await (HonorCancellation ? _flushHold.Task.WaitAsync(cancellationToken) : _flushHold.Task);
            }
        }

        public override async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCalls);
            if (DisposalDelay > TimeSpan.Zero)
            {
                await Task.Delay(DisposalDelay);
            }

            _disposed.TrySetResult();
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A read-only source whose reads complete only when the test releases a chunk or completes the stream.</summary>
    private sealed class ControlledSource : Stream
    {
        private readonly SemaphoreSlim _available = new(0);
        private readonly Queue<byte[]> _chunks = new();

        public void Release(byte[] chunk)
        {
            lock (_chunks)
            {
                _chunks.Enqueue(chunk);
            }

            _available.Release();
        }

        public void Complete() => _available.Release();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _available.WaitAsync(cancellationToken);
            byte[]? chunk;
            lock (_chunks)
            {
                _chunks.TryDequeue(out chunk);
            }

            if (chunk is null)
            {
                return 0;
            }

            chunk.CopyTo(buffer);
            return chunk.Length;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
