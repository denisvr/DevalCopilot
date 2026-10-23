namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// The root facts repeated on every row of the one admission-history statement (ADR-0063): the run, its one sequence, the pinned
/// workspace and initial checkpoint it names, and two bounded counts of the sequence's recipes and steps. A null sequence identity means
/// the run has no sequence. <see cref="StateCode"/> is 1 to 10 for a known state and 0 otherwise, <see cref="AdmissionModeCode"/> 1 or 2
/// for a known mode and 0 otherwise; a number is null unless the stored value is an exact integer in its supported range. No settings
/// snapshot, confirmation, plan-input digest, detail or final reference is selected.
/// </summary>
internal sealed record LocalExecutionAdmissionHistoryRoot(
    Guid RunId,
    Guid RunProjectId,
    string? StoredMode,
    Guid? SequenceId,
    Guid? SequenceRunId,
    Guid? SequenceProjectId,
    Guid? WorkspaceId,
    Guid? CheckpointId,
    string? Fingerprint,
    int StateCode,
    string? ReasonCode,
    bool ReasonOversized,
    int? RecipeCount,
    long? Revision,
    DateTimeOffset? ArmedAtUtc,
    DateTimeOffset? AdvancementDeadlineUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? EndedAtUtc,
    int AdmissionModeCode,
    Guid? WorkspaceProjectId,
    Guid? InitialCheckpointWorkspaceId,
    string? InitialCheckpointFingerprint,
    int RecipeRows,
    int StepRows);
