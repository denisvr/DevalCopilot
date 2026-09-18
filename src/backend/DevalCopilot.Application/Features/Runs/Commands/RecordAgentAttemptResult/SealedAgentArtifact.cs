using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;

/// <summary>Already-sealed, already-hashed evidence for one Agent attempt artifact. Sealing and
/// hashing always happen before this record exists — never inside the database transaction. Mirrors
/// <c>SealedOutputArtifact</c> from the Process-attempt slice exactly.</summary>
public sealed record SealedAgentArtifact(
    ArtifactPurpose Purpose, string RelativeStoragePath, long ByteLength, string ContentHash, bool Truncated);
