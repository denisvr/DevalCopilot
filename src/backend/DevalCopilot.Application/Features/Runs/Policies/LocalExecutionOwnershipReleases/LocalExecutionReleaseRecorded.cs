namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>The one coherent release receipt (ADR-0065): its identity and time, the exact confirmation it was made under, and the
/// sequence number of the one Human event that recorded it.</summary>
internal sealed record LocalExecutionReleaseRecorded(
    Guid ReleaseId,
    DateTimeOffset ReleasedAtUtc,
    long EventSequence,
    int ConfirmationVersion,
    string ConfirmationSha256,
    DateTimeOffset ConfirmationExpiresAtUtc);
