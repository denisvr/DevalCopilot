using System.Text;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// Scans a bounded number of stdout bytes for complete JSONL lines. Once a line exceeds its
/// own byte limit, every byte through its newline is discarded; a later chunk can never make
/// the suffix of that oversized line look like a separate valid response.
/// </summary>
internal sealed class BoundedJsonLineScanner(Stream stream, int maxTotalBytes, int maxLineBytes)
{
    private readonly List<byte> _pendingLine = [];
    private readonly Queue<string> _completeLines = new();
    private int _totalBytesRead;
    private bool _discardingOversizedLine;
    private bool _endOfStream;

    public bool IsExhausted => _endOfStream || _totalBytesRead >= maxTotalBytes;

    /// <summary>Returns complete lines already read, without waiting for more process output.</summary>
    public IReadOnlyList<string> DrainCompleteLines()
    {
        var lines = new List<string>(_completeLines.Count);
        while (_completeLines.TryDequeue(out var line))
        {
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>Reads at most the remaining total-byte allowance and parses each byte once.</summary>
    public async Task<bool> FillAsync(CancellationToken cancellationToken)
    {
        if (IsExhausted)
        {
            return false;
        }

        var buffer = new byte[Math.Min(8192, maxTotalBytes - _totalBytesRead)];
        var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (bytesRead == 0)
        {
            _endOfStream = true;
            return false;
        }

        _totalBytesRead += bytesRead;
        foreach (var value in buffer.AsSpan(0, bytesRead))
        {
            if (value == (byte)'\n')
            {
                if (!_discardingOversizedLine && _pendingLine.Count > 0)
                {
                    var text = Encoding.UTF8.GetString(_pendingLine.ToArray()).TrimEnd('\r');
                    if (text.Length > 0)
                    {
                        _completeLines.Enqueue(text);
                    }
                }

                _pendingLine.Clear();
                _discardingOversizedLine = false;
                continue;
            }

            if (_discardingOversizedLine)
            {
                continue;
            }

            if (_pendingLine.Count == maxLineBytes)
            {
                _pendingLine.Clear();
                _discardingOversizedLine = true;
                continue;
            }

            _pendingLine.Add(value);
        }

        return true;
    }
}
