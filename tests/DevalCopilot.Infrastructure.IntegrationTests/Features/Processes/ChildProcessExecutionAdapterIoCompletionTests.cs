using System.Diagnostics;
using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Processes;
using DevalCopilot.Infrastructure.Features.Processes;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Processes;

/// <summary>
/// ADR-0072: the bounded I/O completion of <see cref="ChildProcessExecutionAdapter"/> against real owned fixture processes, and
/// the write gate of <see cref="BoundedOutputCapture"/> against controlled streams. No provider executable is involved.
/// </summary>
public sealed class ChildProcessExecutionAdapterIoCompletionTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");

    /// <summary>The five-second completion window, the one-second release window and a generous scheduling margin.</summary>
    private static readonly TimeSpan BoundedFinalization = TimeSpan.FromSeconds(12);

    private readonly ChildProcessExecutionAdapter _adapter = new();
    private readonly string _approvedRoot;

    public ChildProcessExecutionAdapterIoCompletionTests()
    {
        _approvedRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-process-io-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_approvedRoot);
        Assert.True(File.Exists(FixtureExecutablePath), $"Expected the fixture executable at '{FixtureExecutablePath}'.");
    }

    [Fact]
    public async Task A_normal_exit_without_input_completes_both_streams_and_reports_input_not_redirected()
    {
        var result = await _adapter.ExecuteAsync(CreateRequest(["write-bytes", "10", "5"]), CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
        Assert.Equal(ProcessIoCompletion.CompleteWithoutInput, result.IoCompletion);
        Assert.True(result.IoCompletion.IsComplete);
    }

    [Fact]
    public async Task Delivered_input_and_ended_streams_are_complete()
    {
        var request = CreateRequest(["echo-stdin"]) with { StandardInput = Encoding.UTF8.GetBytes("payload") };

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessIoCompletion.CompleteWithInput, result.IoCompletion);
        Assert.Equal("payload", result.StandardOutput);
    }

    [Fact]
    public async Task Byte_cap_truncation_is_independent_of_stream_completion()
    {
        var request = CreateRequest(["write-bytes", "4096", "0"]) with { MaxBytesPerStream = 16, MaxTotalCapturedBytes = 16 };

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.StandardOutputTruncated);
        Assert.Equal(OutputCaptureCompletion.Complete, result.IoCompletion.StandardOutput);
        Assert.True(result.IoCompletion.IsComplete);
    }

    [Fact]
    public async Task Input_the_child_never_reads_before_exiting_is_a_faulted_delivery_beside_a_physical_exit_zero()
    {
        // Larger than any anonymous pipe buffer, so the write cannot complete into the buffer before the child exits.
        var request = CreateRequest(["exit-code", "0"]) with { StandardInput = new byte[ProcessExecutionRequest.MaxStandardInputBytes] };

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(InputDeliveryCompletion.Faulted, result.IoCompletion.StandardInput);
        Assert.False(result.IoCompletion.IsComplete);
    }

    [Fact]
    public async Task Input_still_blocked_at_the_execution_deadline_is_deadline_exceeded()
    {
        var request = CreateRequest(["sleep-then-echo-stdin", "20000"], TimeSpan.FromMilliseconds(400)) with
        {
            StandardInput = new byte[ProcessExecutionRequest.MaxStandardInputBytes],
        };

        var result = await _adapter.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.TimedOut, result.Outcome);
        Assert.Equal(InputDeliveryCompletion.DeadlineExceeded, result.IoCompletion.StandardInput);
        Assert.False(result.IoCompletion.IsComplete);
    }

    [Fact]
    public async Task A_killed_tree_reaches_end_of_file_and_keeps_its_timeout_outcome()
    {
        var result = await _adapter.ExecuteAsync(CreateRequest(["spawn-tree", "20000"], TimeSpan.FromMilliseconds(300)), CancellationToken.None);

        Assert.Equal(ProcessExecutionOutcome.TimedOut, result.Outcome);
        Assert.Null(result.ExitCode);
        Assert.Equal(OutputCaptureCompletion.Complete, result.IoCompletion.StandardOutput);
    }

    [Theory]
    [InlineData("both", true, true)]
    [InlineData("stdout", true, false)]
    [InlineData("stderr", false, true)]
    public async Task A_descendant_holding_an_inherited_pipe_after_a_zero_exit_is_bounded_and_recorded_incomplete(
        string heldStreams, bool standardOutputHeld, bool standardErrorHeld)
    {
        var descendantPidFile = Path.Combine(_approvedRoot, $"descendant-{heldStreams}.pid");
        var standardOutputSink = Path.Combine(_approvedRoot, "stdout.partial");
        var standardErrorSink = Path.Combine(_approvedRoot, "stderr.partial");
        var request = CreateRequest(["orphan-holds-pipes", heldStreams, descendantPidFile, "60000"], TimeSpan.FromSeconds(30)) with
        {
            StandardOutputSinkPath = standardOutputSink,
            StandardErrorSinkPath = standardErrorSink,
        };

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await _adapter.ExecuteAsync(request, CancellationToken.None);
            stopwatch.Stop();

            // The physical facts stay exactly as observed: the invoked process exited zero.
            Assert.Equal(ProcessExecutionOutcome.Exited, result.Outcome);
            Assert.Equal(0, result.ExitCode);
            Assert.NotNull(result.Termination);
            Assert.True(stopwatch.Elapsed < BoundedFinalization, $"Finalization took {stopwatch.Elapsed}.");
            Assert.True(stopwatch.Elapsed >= ChildProcessExecutionAdapter.IoCompletionWindow - TimeSpan.FromMilliseconds(250));

            Assert.Equal(
                standardOutputHeld ? OutputCaptureCompletion.DeadlineExceeded : OutputCaptureCompletion.Complete,
                result.IoCompletion.StandardOutput);
            Assert.Equal(
                standardErrorHeld ? OutputCaptureCompletion.DeadlineExceeded : OutputCaptureCompletion.Complete,
                result.IoCompletion.StandardError);
            Assert.Equal(InputDeliveryCompletion.NotRedirected, result.IoCompletion.StandardInput);
            Assert.False(result.IoCompletion.IsComplete);
            Assert.True(result.IoCompletion.DeadlineExceeded);

            // A held stream never releases its redaction tail; a completed one is finalized normally.
            Assert.Equal(standardOutputHeld ? string.Empty : "parent-marker\n", result.StandardOutput);
            Assert.Equal(standardErrorHeld ? string.Empty : "parent-error-marker\n", result.StandardError);

            // Both writers are closed: the partial sinks can be opened exclusively and do not grow afterwards.
            var lengths = (new FileInfo(standardOutputSink).Length, new FileInfo(standardErrorSink).Length);
            await using (File.Open(standardOutputSink, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await using (File.Open(standardErrorSink, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
            Assert.Equal(lengths, (new FileInfo(standardOutputSink).Length, new FileInfo(standardErrorSink).Length));
        }
        finally
        {
            KillDescendant(descendantPidFile);
        }
    }

    [Fact]
    public async Task A_chunk_arriving_after_the_writer_closed_is_dropped_from_the_buffer_and_the_sink()
    {
        var sinkPath = Path.Combine(_approvedRoot, "late.partial");
        var source = new ControlledStream();
        var capture = new BoundedOutputCapture(1024, new SharedCaptureBudget(1024), sinkPath);
        var drain = capture.DrainAsync(source, CancellationToken.None);

        source.Release(Encoding.UTF8.GetBytes(new string('A', 300)));
        await source.WaitUntilConsumedAsync();
        capture.MarkDeadlineExceeded();
        Assert.True(await capture.CloseAsync(TimeSpan.FromSeconds(1)));
        var sealedText = capture.Text;
        var sealedLength = new FileInfo(sinkPath).Length;

        source.Release(Encoding.UTF8.GetBytes(new string('B', 300)));
        source.Complete();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(sealedText, capture.Text);
        Assert.DoesNotContain('B', capture.Text);
        Assert.Equal(sealedLength, new FileInfo(sinkPath).Length);
        Assert.Equal(OutputCaptureCompletion.DeadlineExceeded, capture.GetCompletion(writerClosed: true));
    }

    [Fact]
    public async Task A_read_fault_is_faulted_and_never_complete()
    {
        var capture = new BoundedOutputCapture(1024, new SharedCaptureBudget(1024));

        await capture.DrainAsync(new FaultingStream(), CancellationToken.None);

        Assert.True(await capture.CloseAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(OutputCaptureCompletion.Faulted, capture.GetCompletion(writerClosed: true));
    }

    [Fact]
    public void A_writer_that_was_not_proved_closed_is_unknown()
    {
        var capture = new BoundedOutputCapture(1024, new SharedCaptureBudget(1024));

        Assert.Equal(OutputCaptureCompletion.Unknown, capture.GetCompletion(writerClosed: false));
    }

    [Fact]
    public async Task An_incomplete_stream_never_releases_a_split_secret_held_in_the_redaction_tail()
    {
        var source = new ControlledStream();
        var capture = new BoundedOutputCapture(4096, new SharedCaptureBudget(4096));
        var drain = capture.DrainAsync(source, CancellationToken.None);

        // The token's prefix arrives, its remainder never does.
        source.Release(Encoding.UTF8.GetBytes(new string('A', 200) + "token ghp_abcdefghij"));
        await source.WaitUntilConsumedAsync();
        capture.MarkDeadlineExceeded();
        Assert.True(await capture.CloseAsync(TimeSpan.FromSeconds(1)));
        source.Complete();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain("ghp_", capture.Text);
        Assert.DoesNotContain("token", capture.Text);
        Assert.Equal(OutputCaptureCompletion.DeadlineExceeded, capture.GetCompletion(writerClosed: true));
    }

    public void Dispose()
    {
        if (Directory.Exists(_approvedRoot))
        {
            try
            {
                Directory.Delete(_approvedRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private ProcessExecutionRequest CreateRequest(IReadOnlyList<string> arguments, TimeSpan? timeout = null) => new()
    {
        ExecutablePath = FixtureExecutablePath,
        Arguments = arguments,
        WorkingDirectory = _approvedRoot,
        ApprovedRoot = _approvedRoot,
        Timeout = timeout ?? TimeSpan.FromSeconds(10),
    };

    private static void KillDescendant(string pidFile)
    {
        if (!File.Exists(pidFile) || !int.TryParse(File.ReadAllText(pidFile), out var processId))
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
            process.WaitForExit(5000);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
        }
    }

    /// <summary>A read-only stream whose reads complete only when the test releases a chunk or completes the stream.</summary>
    private sealed class ControlledStream : Stream
    {
        private readonly SemaphoreSlim _available = new(0);
        private readonly SemaphoreSlim _consumed = new(0);
        private readonly Queue<byte[]> _chunks = new();

        public void Release(byte[] chunk)
        {
            lock (_chunks)
            {
                _chunks.Enqueue(chunk);
            }

            _available.Release();
        }

        public void Complete()
        {
            _available.Release();
        }

        public Task WaitUntilConsumedAsync() => _consumed.WaitAsync(TimeSpan.FromSeconds(5));

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
                // Only Complete releases a read without a chunk: end-of-file.
                return 0;
            }

            chunk.CopyTo(buffer);
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                _consumed.Release();
            });
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

    private sealed class FaultingStream : Stream
    {
        private int _reads;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_reads++ == 0)
            {
                buffer.Span[0] = (byte)'A';
                return ValueTask.FromResult(1);
            }

            throw new IOException("Simulated pipe failure.");
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
