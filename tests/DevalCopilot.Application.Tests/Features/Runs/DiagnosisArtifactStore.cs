using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>An <see cref="IArtifactStore"/> for the verification-diagnosis tests. Manifests are written to a real temp
/// file and sealed from those exact bytes (so a test reads the sealed manifest back), every seal and every orphan cleanup
/// is recorded, and sealed verification output is served the way the real store serves it: a registered path whose length
/// and hash match is read as a bounded window that never splits a trailing code point (and decodes invalid bytes with the
/// replacement character), an unregistered path is Missing, and a mismatch is an IntegrityMismatch.</summary>
internal sealed class DiagnosisArtifactStore : IArtifactStore
{
    private static readonly string PartialRoot = Path.Combine(Path.GetTempPath(), "devalcopilot-app-tests-diagnosis-partials");

    private readonly List<(Guid RunId, Guid AttemptId, ArtifactPurpose Purpose)> _sealed = [];
    private readonly Dictionary<string, byte[]> _sealedOutput = new(StringComparer.Ordinal);

    public bool SealShouldFail { get; set; }

    public int SealCount => _sealed.Count;

    public int VerifyCalls { get; private set; }

    public HashSet<(Guid RunId, Guid AttemptId, ArtifactPurpose Purpose)> DeletedSealedFiles { get; } = [];

    public IReadOnlyList<Guid> SealedAttemptIds => _sealed.Select(entry => entry.AttemptId).ToArray();

    /// <summary>The hash the real store computes and the verification result records.</summary>
    public static string HashOf(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Registers sealed verification output and returns the (path, length, hash) its row records.</summary>
    public (string Path, long Length, string Hash) RegisterSealedOutput(byte[] bytes)
    {
        var path = $"verifications/{Guid.NewGuid()}/output.sealed";
        _sealedOutput[path] = bytes;
        return (path, bytes.LongLength, HashOf(bytes));
    }

    /// <summary>Replaces the bytes behind a registered path, so the recorded length and hash no longer match.</summary>
    public void Overwrite(string path, byte[] bytes) => _sealedOutput[path] = bytes;

    /// <summary>Makes a registered sealed output unreadable, as a deleted file is.</summary>
    public void Remove(string path) => _sealedOutput.Remove(path);

    public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
        Path.Combine(PartialRoot, $"{runId:N}", $"{attemptId:N}", $"{purpose}.partial");

    public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
        $"{runId:N}/{attemptId:N}/{purpose}.sealed";

    /// <summary>The sealed manifest text of one attempt.</summary>
    public string ReadManifest(Guid runId, Guid attemptId) =>
        File.ReadAllText(GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest));

    /// <summary>Runs once after the first successful seal: a change committed here lands strictly between a handler's manifest
    /// seal and everything it does next.</summary>
    public Func<Task>? AfterSeal { get; set; }

    public async Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken)
    {
        if (SealShouldFail)
        {
            return null;
        }

        var partialPath = GetPartialPath(runId, attemptId, purpose);
        var bytes = File.Exists(partialPath) ? File.ReadAllBytes(partialPath) : [];
        _sealed.Add((runId, attemptId, purpose));
        if (AfterSeal is { } hook)
        {
            AfterSeal = null;
            await hook();
        }

        return new SealedOutputFile(GetSealedRelativePath(runId, attemptId, purpose), bytes.LongLength, HashOf(bytes));
    }

    public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;

    public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;

    public Task<SealedOutputFile?> DescribeSealedFileAsync(
        Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
        Task.FromResult<SealedOutputFile?>(null);

    public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose)
    {
    }

    public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
        DeletedSealedFiles.Add((runId, attemptId, purpose));

    public Task<PartialReadWindow> ReadPartialAsync(
        Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
        Task.FromResult(new PartialReadWindow(string.Empty, fromOffset, 0));

    public Task<SealedReadWindow> VerifyAndReadSealedAsync(
        string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
        CancellationToken cancellationToken)
    {
        VerifyCalls++;
        if (!_sealedOutput.TryGetValue(relativeStoragePath, out var bytes))
        {
            return Task.FromResult(new SealedReadWindow(SealedReadStatus.Missing, string.Empty, fromOffset, 0));
        }

        if (bytes.LongLength != expectedByteLength || !string.Equals(HashOf(bytes), expectedContentHash, StringComparison.Ordinal))
        {
            return Task.FromResult(new SealedReadWindow(SealedReadStatus.IntegrityMismatch, string.Empty, fromOffset, bytes.LongLength));
        }

        if (fromOffset >= bytes.LongLength)
        {
            return Task.FromResult(new SealedReadWindow(SealedReadStatus.Ok, string.Empty, fromOffset, bytes.LongLength));
        }

        var read = (int)Math.Min(maxBytes, bytes.LongLength - fromOffset);
        var reachedTrueEnd = fromOffset + read >= bytes.LongLength;
        var decodable = reachedTrueEnd ? read : DecodableLength(bytes.AsSpan((int)fromOffset, read));
        return Task.FromResult(new SealedReadWindow(
            SealedReadStatus.Ok, Encoding.UTF8.GetString(bytes, (int)fromOffset, decodable), fromOffset + decodable, bytes.LongLength));
    }

    /// <summary>The longest prefix that does not end inside a valid multi-byte sequence.</summary>
    private static int DecodableLength(ReadOnlySpan<byte> window)
    {
        var length = window.Length;
        for (var back = 1; back <= Math.Min(3, length); back++)
        {
            var lead = window[length - back];
            if ((lead & 0b1100_0000) == 0b1000_0000)
            {
                continue;
            }

            var needed = lead switch
            {
                >= 0xF0 and <= 0xF4 => 4,
                >= 0xE0 and <= 0xEF => 3,
                >= 0xC2 and <= 0xDF => 2,
                _ => 1,
            };
            return needed > back ? length - back : length;
        }

        return length;
    }
}
