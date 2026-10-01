namespace DevalCopilot.Api.Features.Projects.GetProjectRunSummaries;

/// <param name="HeadState">Null only when the project has no repository baseline at all yet —
/// rendered by the frontend as "not yet validated," never fabricated clean/branch data.</param>
/// <param name="ExecutionMode">Legacy, Simulated, ManualAgent, or Unrecognized for the summarized run; null when
/// the project has no run.</param>
/// <param name="CanCreateRun">A hint that the project has no run or only terminal runs; creation re-checks it.</param>
public sealed record ProjectRunSummaryResponse(
    Guid ProjectId,
    string ProjectName,
    string CanonicalPath,
    Guid? RunId,
    int? ExecutionNumber,
    string? Lifecycle,
    string? Stage,
    IReadOnlyList<CapabilityReadinessResponse> Capabilities,
    string? HeadState,
    string? BranchName,
    string? HeadCommitSha,
    bool IsDirty,
    DateTimeOffset? BaselineObservedAtUtc,
    string? ExecutionMode,
    bool CanCreateRun);
