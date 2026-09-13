using System.Text;

namespace DevalCopilot.Infrastructure.Features.Processes;

/// <summary>
/// Captures one process stream up to its own cap and the cross-stream shared budget. Always
/// reads the source stream through to its natural end regardless of whether bytes are still
/// being stored: once the cap is reached, excess bytes are discarded rather than kept, but the
/// read loop keeps draining so the child process never blocks writing to a pipe nobody is
/// reading and can still exit normally (or be killed and reach end-of-stream) on its own.
/// </summary>
internal sealed class BoundedOutputCapture(int streamCap, SharedCaptureBudget sharedBudget)
{
    private readonly MemoryStream _buffer = new();
    private bool _truncated;

    public async Task DrainAsync(Stream source)
    {
        var readBuffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(readBuffer).ConfigureAwait(false)) > 0)
        {
            var streamRemaining = Math.Max(streamCap - (int)_buffer.Length, 0);
            var requested = Math.Min(bytesRead, streamRemaining);
            var granted = requested > 0 ? sharedBudget.Reserve(requested) : 0;

            if (granted > 0)
            {
                _buffer.Write(readBuffer, 0, granted);
            }

            if (granted < bytesRead)
            {
                _truncated = true;
            }
        }
    }

    public string Text => Encoding.UTF8.GetString(_buffer.ToArray());

    public bool Truncated => _truncated;
}
