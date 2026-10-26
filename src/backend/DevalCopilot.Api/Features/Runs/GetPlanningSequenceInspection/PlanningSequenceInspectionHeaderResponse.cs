namespace DevalCopilot.Api.Features.Runs.GetPlanningSequenceInspection;

/// <summary>
/// The stored bookkeeping of the recorded planning sequence (ADR-0068): never effective live control, an acceptance or approval proof, a
/// preparation token, an objective or a detail code. <c>Revision</c> is a canonical Int64 string and times are UTC round-trip strings.
/// </summary>
public sealed record PlanningSequenceInspectionHeaderResponse(
    Guid SequenceId,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    string RecordedState,
    string? ReasonCode,
    string Revision,
    string ArmedAtUtc,
    string UpdatedAtUtc,
    string? StopRequestedAtUtc,
    string? EndedAtUtc);
