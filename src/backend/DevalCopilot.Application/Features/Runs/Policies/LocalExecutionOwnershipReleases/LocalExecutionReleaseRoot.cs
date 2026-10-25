namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>
/// The root facts of the one release statement: the run, the identity of its (at most two read) sequences, the pinned workspace and two
/// bounded counts of the sequence's recipes and steps and whether the run is a ManualAgent run (the only mode that owns a local sequence). A null sequence identity means the run has no sequence. <see cref="StateCode"/>
/// is 1 to 10 for a known state and 0 otherwise, <see cref="AdmissionModeCode"/> 1 or 2 for a known mode and 0 otherwise.
/// </summary>
internal sealed record LocalExecutionReleaseRoot(
    Guid RunId,
    Guid RunProjectId,
    Guid? SequenceId,
    Guid? SequenceRunId,
    Guid? SequenceProjectId,
    Guid? SequenceWorkspaceId,
    int StateCode,
    int AdmissionModeCode,
    int? RecipeCount,
    Guid? WorkspaceProjectId,
    int RecipeRows,
    int StepRows,
    bool RunIsManualAgent);
