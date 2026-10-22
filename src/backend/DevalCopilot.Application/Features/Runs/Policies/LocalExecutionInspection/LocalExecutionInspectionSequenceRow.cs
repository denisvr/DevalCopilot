namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The bounded scalar facts of the one sequence of a run, classified by the database. <see cref="PlanAuthorityCode"/> is 1 to 3 for a known
/// authority and 0 otherwise, <see cref="StateCode"/> 1 to 9 for a known state and 0 otherwise. A number is null unless the stored value is
/// an exact integer in its supported range, and a text is read only within its byte guard and flagged when oversized. No settings snapshot,
/// confirmation, plan-input digest, detail code or final reference is selected.
/// </summary>
internal sealed record LocalExecutionInspectionSequenceRow(
    Guid Id,
    Guid RunId,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CheckpointId,
    string? Fingerprint,
    Guid PlanProposalMessageId,
    int PlanAuthorityCode,
    int StateCode,
    string? ReasonCode,
    bool ReasonOversized,
    int? RecipeCount,
    int? CorrectionAllowance,
    long? Revision,
    DateTimeOffset ArmedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? EndedAtUtc);
