using System.Text;

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
/// type reads from) — so once <see cref="DrainAsync"/> returns, the handle is guaranteed closed
/// and the caller may safely seal (rename) the file.
/// </summary>
internal sealed class BoundedOutputCapture : IAsyncDisposable
{
    private readonly int _streamCap;
    private readonly SharedCaptureBudget _sharedBudget;
    private readonly StreamingOutputRedactor _redactor = new();
    private readonly MemoryStream _buffer = new();
    private readonly FileStream? _sink;
    private bool _truncated;

    public BoundedOutputCapture(int streamCap, SharedCaptureBudget sharedBudget, string? sinkPath = null)
    {
        _streamCap = streamCap;
        _sharedBudget = sharedBudget;

        if (sinkPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sinkPath)!);

            // FileShare.Read only: no other writer is ever legitimate, and a concurrent live
            // reader opens its own handle (see FilesystemArtifactStore) with the sharing flags
            // it needs — this stream's own share mode does not need to anticipate that.
            _sink = new FileStream(sinkPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        }
    }

    public async Task DrainAsync(Stream source)
    {
        var readBuffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(readBuffer).ConfigureAwait(false)) > 0)
        {
            await AcceptAsync(_redactor.ProcessChunk(readBuffer.AsSpan(0, bytesRead))).ConfigureAwait(false);
        }

        await AcceptAsync(_redactor.Flush()).ConfigureAwait(false);

        if (_sink is not null)
        {
            await _sink.FlushAsync().ConfigureAwait(false);
        }
    }

    private async Task AcceptAsync(byte[] redactedChunk)
    {
        if (redactedChunk.Length == 0)
        {
            return;
        }

        var streamRemaining = Math.Max(_streamCap - (int)_buffer.Length, 0);
        var requested = Math.Min(redactedChunk.Length, streamRemaining);
        var granted = requested > 0 ? _sharedBudget.Reserve(requested) : 0;

        if (granted > 0)
        {
            _buffer.Write(redactedChunk, 0, granted);

            if (_sink is not null)
            {
                // Flushed immediately, not just once at the end of DrainAsync: FileStream
                // buffers writes internally, and a live reader (the cockpit's output poll,
                // reading through its own independently opened handle) must be able to observe
                // accepted bytes promptly rather than only once the whole capture finishes.
                await _sink.WriteAsync(redactedChunk.AsMemory(0, granted)).ConfigureAwait(false);
                await _sink.FlushAsync().ConfigureAwait(false);
            }
        }

        if (granted < redactedChunk.Length)
        {
            _truncated = true;
        }
    }

    public string Text => Encoding.UTF8.GetString(_buffer.ToArray());

    public bool Truncated => _truncated;

    /// <summary>Closes the sink's write handle, if any. Idempotent. Must complete before the
    /// caller attempts to seal (rename) the sink file.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_sink is not null)
        {
            await _sink.DisposeAsync().ConfigureAwait(false);
        }
    }
}
