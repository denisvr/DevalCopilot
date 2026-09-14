using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordProcessAttemptResult;

/// <summary>
/// One stream's already-sealed, already-hashed output file, ready to become a durable
/// <see cref="Artifact"/> row in the same transaction that records the attempt's terminal
/// state. Sealing and hashing always happen before this record exists — never inside the
/// database transaction.
/// </summary>
public sealed record SealedOutputArtifact(
    ArtifactPurpose Purpose, string RelativeStoragePath, long ByteLength, string ContentHash, bool Truncated);
