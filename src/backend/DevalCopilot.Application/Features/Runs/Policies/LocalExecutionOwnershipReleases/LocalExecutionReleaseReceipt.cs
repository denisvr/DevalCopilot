namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>The stored release (with its typed intent snapshot, ADR-0066) and the header of the one Human run event it names. The event
/// is audit data, never authority: the proof requires the actual identity, run, type and actor.</summary>
internal sealed record LocalExecutionReleaseReceipt(
    Guid? Id,
    Guid? WitnessKeyedId,
    Guid? WitnessId,
    Guid? ObservationId,
    Guid? StepId,
    Guid? EventId,
    DateTimeOffset? ReleasedAtUtc,
    int? ConfirmationVersion,
    string? ConfirmationSha256,
    DateTimeOffset? ConfirmationExpiresAtUtc,
    Guid? EventHeaderId,
    Guid? EventRunId,
    long? EventSequence,
    bool EventIsRelease,
    bool EventIsHuman,
    LocalExecutionReleaseIntent Intent);
