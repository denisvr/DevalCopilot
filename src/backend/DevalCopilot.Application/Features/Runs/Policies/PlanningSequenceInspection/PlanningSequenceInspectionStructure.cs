namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>The whole proved structural set of one run's recorded planning sequence: its header, the recorded steps in ordinal order and
/// the run's project, all from historical identities and never the latest workspace, checkpoint or message.</summary>
internal sealed record PlanningSequenceInspectionStructure(
    Guid RunId,
    Guid ProjectId,
    PlanningSequenceInspectionHeader Header,
    IReadOnlyList<PlanningSequenceInspectionStep> Steps);
