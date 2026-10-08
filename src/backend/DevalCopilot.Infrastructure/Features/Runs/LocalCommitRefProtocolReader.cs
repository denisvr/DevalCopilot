using System.Text;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Reads the acknowledgement protocol of the owned <c>update-ref --stdin</c> child from its actual standard-output byte stream and
/// admits only the exact supported form: printable ASCII terminated by a single LF, at most <c>maxLineBytes</c> before the LF and
/// <c>maxTotalBytes</c> in total. A carriage return, NUL, other control byte, non-ASCII byte, overlong line or EOF inside a line is a
/// distinct fault; nothing is stripped, decoded leniently or truncated. Byte counts are enforced while reading, so an unterminated
/// line can never grow beyond its bound. After any cancellation the abandoned read may still hold the stream, so the reader refuses
/// further use.
/// </summary>
internal sealed class LocalCommitRefProtocolReader(Stream stream, int maxLineBytes, int maxTotalBytes)
{
    internal enum Kind
    {
        Line,
        Eof,
        Overflow,
        Malformed,
        Cancelled,
    }

    internal readonly record struct LineResult(Kind Kind, string Text);

    private readonly byte[] buffer = new byte[256];
    private int start;
    private int end;
    private long total;

    /// <summary>Bytes the child sent beyond what the protocol has asked for; after an acknowledgement this is always a fault.</summary>
    internal bool HasPendingBytes => start < end;

    /// <summary>True once a read was cancelled or faulted; the stream must not be read again.</summary>
    internal bool Unusable { get; private set; }

    internal async Task<LineResult> ReadLineAsync(CancellationToken token)
    {
        if (Unusable)
        {
            return new LineResult(Kind.Eof, string.Empty);
        }

        var line = new byte[maxLineBytes];
        var length = 0;
        try
        {
            while (true)
            {
                if (start == end && !await FillAsync(token))
                {
                    return new LineResult(Kind.Eof, string.Empty);
                }

                if (total > maxTotalBytes)
                {
                    return new LineResult(Kind.Overflow, string.Empty);
                }

                var value = buffer[start++];
                if (value == (byte)'\n')
                {
                    return new LineResult(Kind.Line, Encoding.ASCII.GetString(line, 0, length));
                }

                if (value is < 0x20 or > 0x7E)
                {
                    return new LineResult(Kind.Malformed, string.Empty);
                }

                if (length == maxLineBytes)
                {
                    return new LineResult(Kind.Overflow, string.Empty);
                }

                line[length++] = value;
            }
        }
        catch (OperationCanceledException)
        {
            Unusable = true;
            return new LineResult(Kind.Cancelled, string.Empty);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Unusable = true;
            return new LineResult(Kind.Eof, string.Empty);
        }
    }

    /// <summary>After the child has exited, reports whether any unsolicited byte remains: true when a byte is pending or arrives, false
    /// only when a read actually returned end of file, and <c>null</c> when the read was cancelled, faulted or the stream is already
    /// unusable. A null proves nothing, so a caller must never treat it as the absence of trailing output.</summary>
    internal async Task<bool?> HasTrailingBytesAsync(CancellationToken token)
    {
        if (HasPendingBytes)
        {
            return true;
        }

        if (Unusable)
        {
            return null;
        }

        try
        {
            return await FillAsync(token);
        }
        catch (OperationCanceledException)
        {
            Unusable = true;
            return null;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Unusable = true;
            return null;
        }
    }

    private async Task<bool> FillAsync(CancellationToken token)
    {
        // WaitAsync guarantees the caller regains control at cancellation even if the pipe read itself is not interruptible.
        var read = await stream.ReadAsync(buffer.AsMemory(), token).AsTask().WaitAsync(token);
        if (read == 0)
        {
            return false;
        }

        total += read;
        start = 0;
        end = read;
        return true;
    }
}
