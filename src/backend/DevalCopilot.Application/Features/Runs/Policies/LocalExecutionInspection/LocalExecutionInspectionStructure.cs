using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>The whole proved structural set of one run's recorded sequence: its header, the recorded steps in ordinal order and the
/// run's project, all from historical identities and never the latest recipe, message or checkpoint.</summary>
internal sealed record LocalExecutionInspectionStructure(
    Guid RunId,
    Guid ProjectId,
    LocalExecutionInspectionHeader Header,
    IReadOnlyList<LocalExecutionInspectionStep> Steps);
