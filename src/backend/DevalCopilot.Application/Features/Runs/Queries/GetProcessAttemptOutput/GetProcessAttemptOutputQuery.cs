using Devalente.Shared.Cqrs;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetProcessAttemptOutput;

public sealed record GetProcessAttemptOutputQuery(
    Guid RunId, Guid AttemptId, ArtifactPurpose Purpose, long FromOffset, int MaxBytes)
    : IQuery<GetProcessAttemptOutputQueryResult>;

public enum ProcessAttemptOutputStatus
{
    /// <summary>Bytes returned normally — from the live in-progress capture, or a verified
    /// sealed artifact.</summary>
    Ok,

    /// <summary>No such run/attempt exists.</summary>
    AttemptNotFound,

    /// <summary>The attempt is terminal and no artifact was ever recorded for this stream (for
    /// example, a non-Process attempt, or a stream whose seal failed). Not an error — there is
    /// truthfully nothing to show.</summary>
    NoOutputAvailable,

    /// <summary>The sealed file's actual length or hash no longer matches its durable metadata.
    /// Never presented as trustworthy evidence.</summary>
    IntegrityMismatch,
}

/// <param name="IsFinal">True once the underlying artifact is sealed and recorded — the caller
/// should stop polling.</param>
/// <param name="Truncated">Null exactly when genuinely unknown — an artifact recovered from a
/// host interruption, whose truncation at the point of interruption was never observed.</param>
public sealed record GetProcessAttemptOutputQueryResult(
    ProcessAttemptOutputStatus Status, string Text, long NextOffset, long TotalLengthSoFar, bool IsFinal, bool? Truncated);
