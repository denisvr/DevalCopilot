using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>The stored bookkeeping of the recorded planning sequence (ADR-0068): never effective live control, an acceptance or
/// implementation approval, a preparation owner token, an objective text or digest, or a detail code. The recorded state is not proof of
/// present execution authority.</summary>
public sealed record PlanningSequenceInspectionHeader(
    Guid SequenceId,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    PlanningSequenceState RecordedState,
    string? ReasonCode,
    long Revision,
    DateTimeOffset ArmedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? StopRequestedAtUtc,
    DateTimeOffset? EndedAtUtc);
