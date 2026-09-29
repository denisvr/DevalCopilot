using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetSealedAgentArtifactWindow;

/// <summary>
/// The one artifact-row resolution and sealed read shared by every sealed Agent-artifact window
/// query (the message-linked route and the attempt-history route). Each caller independently owns
/// how it proved the attempt's identity; this reader owns only the closed purpose allowlist, the
/// independent run/attempt/purpose artifact filter, and the delegation to the artifact store's
/// integrity-verifying, containment-checked, byte-capped read — so both routes serve identical
/// bytes and statuses and can never drift apart.
/// </summary>
public static class SealedAgentArtifactWindowReader
{
    /// <summary>The closed set of Agent-attempt artifact purposes these operations ever serve —
    /// never the two Process-attempt purposes, which belong to a different attempt kind and endpoint.</summary>
    public static IReadOnlySet<ArtifactPurpose> AllowlistedPurposes { get; } = new HashSet<ArtifactPurpose>
    {
        ArtifactPurpose.AgentContextManifest,
        ArtifactPurpose.AgentStandardOutput,
        ArtifactPurpose.AgentStandardError,
        ArtifactPurpose.AgentFinalResponse,
    };

    public static async Task<GetSealedAgentArtifactWindowQueryResult> ReadAsync(
        IDevalCopilotDbContext dbContext,
        IArtifactStore artifactStore,
        Guid runId,
        Guid attemptId,
        ArtifactPurpose purpose,
        long fromOffset,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        // Filtered by AttemptId, RunId, AND Purpose — Artifact.RunId is persisted independently of
        // its AttemptId and the database enforces no composite run/attempt ownership constraint
        // between them, so an inconsistent cross-run row must never leak into this window.
        var artifact = await dbContext.Artifacts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.AttemptId == attemptId && candidate.RunId == runId && candidate.Purpose == purpose,
                cancellationToken);

        if (artifact is null)
        {
            return GetSealedAgentArtifactWindowQueryResult.ArtifactNotFound(fromOffset);
        }

        var sealedRead = await artifactStore.VerifyAndReadSealedAsync(
            artifact.RelativeStoragePath, artifact.ByteLength, artifact.ContentHash, fromOffset, maxBytes, cancellationToken);

        var status = sealedRead.Status switch
        {
            SealedReadStatus.Ok => SealedAgentArtifactWindowStatus.Ok,
            SealedReadStatus.Missing => SealedAgentArtifactWindowStatus.Missing,
            SealedReadStatus.IntegrityMismatch => SealedAgentArtifactWindowStatus.IntegrityMismatch,
            _ => throw new ArgumentOutOfRangeException(),
        };

        // Truncated is preserved as-is (including a genuinely unknown null for an artifact
        // recovered from a host interruption) only for a verified Ok read; every other status
        // never carries content, so it never carries a truncation fact about content it isn't
        // returning.
        return new GetSealedAgentArtifactWindowQueryResult(
            status,
            sealedRead.Text,
            sealedRead.NextOffset,
            sealedRead.TotalLengthSoFar,
            status == SealedAgentArtifactWindowStatus.Ok ? artifact.Truncated : null);
    }
}
