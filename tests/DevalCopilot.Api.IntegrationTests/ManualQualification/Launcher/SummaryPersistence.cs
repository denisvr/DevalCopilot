namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The one place a summary file is created, and its checked outcome. The file is created exclusively (never overwritten, never
/// retried), its bytes are written and flushed to the device, and the stream is disposed, all inside one guarded scope: a summary is
/// persisted only when every one of those steps succeeded. Any failure is a typed <see cref="Failed"/> result, never a swallowed
/// message, so finalization can turn it into a closed attention outcome instead of success.
/// </summary>
public static class SummaryPersistence
{
    /// <summary>The closed exit code of an entry whose exchange or observation facts were already printed but whose summary could not
    /// be persisted: attention, never success. A more specific non-success exit code of the entry itself is kept.</summary>
    public const int NotPersistedExitCode = 9;

    /// <summary>True only when the file was created exclusively, fully written, flushed and closed.</summary>
    public static async Task<bool> SaveAsync(string directory, string fileName, string text)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The exit code of a finalization whose persistence outcome is known: a successful or attention-only exit code (0 or 6) is
    /// replaced by <see cref="NotPersistedExitCode"/> when the summary was not persisted; every other non-success code is kept.</summary>
    public static int ExitCode(int reported, bool persisted) =>
        persisted || reported is not (0 or 6) ? reported : NotPersistedExitCode;
}
