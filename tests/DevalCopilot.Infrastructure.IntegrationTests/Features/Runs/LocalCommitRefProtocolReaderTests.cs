using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// The final-stream proof of the reference-transaction protocol reader (ADR-0029 R8). Only a read that actually returns end of file
/// proves there is no unsolicited trailing output; pending bytes are a fault, and a cancelled or faulted read proves nothing.
/// </summary>
public sealed class LocalCommitRefProtocolReaderTests
{
    private static LocalCommitRefProtocolReader Reader(Stream stream) => new(stream, 128, 1024);

    [Fact]
    public async Task A_real_end_of_file_after_the_acknowledgement_proves_there_is_no_trailing_output()
    {
        var reader = Reader(new MemoryStream("commit: ok\n"u8.ToArray()));

        Assert.Equal(LocalCommitRefProtocolReader.Kind.Line, (await reader.ReadLineAsync(CancellationToken.None)).Kind);

        Assert.False(await reader.HasTrailingBytesAsync(CancellationToken.None));
        Assert.False(reader.Unusable);
    }

    [Fact]
    public async Task Bytes_sent_after_the_acknowledgement_are_trailing_output()
    {
        var reader = Reader(new MemoryStream("commit: ok\nsurprise\n"u8.ToArray()));
        await reader.ReadLineAsync(CancellationToken.None);

        Assert.True(await reader.HasTrailingBytesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_read_that_faults_proves_nothing_and_is_neither_true_nor_false()
    {
        var reader = Reader(new ScriptedStream(() => throw new IOException("The pipe was broken.")));

        Assert.Null(await reader.HasTrailingBytesAsync(CancellationToken.None));
        Assert.True(reader.Unusable);
    }

    [Fact]
    public async Task A_read_on_a_disposed_stream_proves_nothing()
    {
        var reader = Reader(new ScriptedStream(() => throw new ObjectDisposedException("pipe")));

        Assert.Null(await reader.HasTrailingBytesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_read_proves_nothing_and_the_reader_refuses_further_use()
    {
        var reader = Reader(new ScriptedStream(null));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.Null(await reader.HasTrailingBytesAsync(cancellation.Token));

        Assert.True(reader.Unusable);
        Assert.Null(await reader.HasTrailingBytesAsync(CancellationToken.None));
    }

    /// <summary>A read-only stream whose reads either fail as scripted or never complete until cancelled.</summary>
    private sealed class ScriptedStream(Func<int>? read) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => read is null
            ? throw new NotSupportedException()
            : read();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (read is not null)
            {
                return read();
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
