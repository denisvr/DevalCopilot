using System.Security.Cryptography;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>An <see cref="IArtifactStore"/> for the format-repair claim tests: manifests are written to a real
/// temp file and sealed from those exact bytes (so a test reads the sealed manifest content back), every seal
/// and every orphan cleanup is recorded, and a seal can be made to fail.</summary>
internal sealed class RepairArtifactStore : IArtifactStore
{
    private static readonly string PartialRoot = Path.Combine(Path.GetTempPath(), "devalcopilot-app-tests-repair-partials");

    private readonly List<(Guid RunId, Guid AttemptId, ArtifactPurpose Purpose)> _sealed = [];

    public bool SealShouldFail { get; set; }

    public int SealCount => _sealed.Count;

    public HashSet<(Guid RunId, Guid AttemptId, ArtifactPurpose Purpose)> DeletedSealedFiles { get; } = [];

    public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
        Path.Combine(PartialRoot, $"{runId:N}", $"{attemptId:N}", $"{purpose}.partial");

    public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
        $"{runId:N}/{attemptId:N}/{purpose}.sealed";

    /// <summary>The sealed manifest text of one attempt.</summary>
    public string ReadManifest(Guid runId, Guid attemptId) =>
        File.ReadAllText(GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest));

    /// <summary>The attempts whose manifest was sealed, in seal order.</summary>
    public IReadOnlyList<Guid> SealedAttemptIds => _sealed.Select(entry => entry.AttemptId).ToArray();

    public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken)
    {
        if (SealShouldFail)
        {
            return Task.FromResult<SealedOutputFile?>(null);
        }

        var partialPath = GetPartialPath(runId, attemptId, purpose);
        var bytes = File.Exists(partialPath) ? File.ReadAllBytes(partialPath) : [];
        _sealed.Add((runId, attemptId, purpose));
        return Task.FromResult<SealedOutputFile?>(new SealedOutputFile(
            GetSealedRelativePath(runId, attemptId, purpose), bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))));
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
        CancellationToken cancellationToken) =>
        Task.FromResult(new SealedReadWindow(SealedReadStatus.Missing, string.Empty, fromOffset, 0));
}
