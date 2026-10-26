namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>
/// The bounded scalar facts of the one sequence of a run, classified by the database. Identifiers and times are the raw stored bytes
/// withheld above their bound (see <see cref="PlanningSequenceInspectionStorage"/>), so corrupt storage reads as an explicit refusal and
/// never as a conversion fault. <see cref="StateCode"/> is 1 to 9 for a known state and 0 otherwise. A number is null unless the stored
/// value is an exact integer in its supported range, and a text is read only within its byte guard and flagged when oversized. The
/// objective digest, detail code and every preparation token are never selected.
/// </summary>
internal sealed record PlanningSequenceInspectionSequenceRow(
    byte[]? Id,
    byte[]? RunId,
    byte[]? ProjectId,
    byte[]? WorkspaceId,
    byte[]? CheckpointId,
    string? Fingerprint,
    int StateCode,
    string? ReasonCode,
    bool ReasonOversized,
    long? Revision,
    byte[]? ArmedAtUtc,
    byte[]? UpdatedAtUtc,
    byte[]? StopRequestedAtUtc,
    bool StopRequestedPresent,
    byte[]? EndedAtUtc,
    bool EndedPresent);
