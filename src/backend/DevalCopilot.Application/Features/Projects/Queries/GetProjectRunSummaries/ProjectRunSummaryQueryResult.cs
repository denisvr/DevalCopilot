using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectRunSummaries;

/// <param name="Capabilities">The same shared host observation, projected for this project's
/// default requirement classification — identical across every project, since the underlying
/// evidence is host-scoped, not project-scoped.</param>
/// <param name="HeadState">Null only when the project has no <see cref="RepositoryBaseline"/> at
/// all yet — a legacy or otherwise never-validated project, rendered as "not yet validated"
/// rather than fabricating clean/branch data for it.</param>
/// <param name="BaselineObservedAtUtc">Display metadata only — never used to select which
/// baseline is current; the greatest <see cref="RepositoryBaseline.BaselineNumber"/> is.</param>
public sealed record ProjectRunSummaryQueryResult(
    Guid ProjectId,
    string ProjectName,
    string CanonicalPath,
    Guid? RunId,
    int? ExecutionNumber,
    RunLifecycle? Lifecycle,
    RunStage? Stage,
    IReadOnlyList<CapabilityReadinessQueryResult> Capabilities,
    RepositoryHeadState? HeadState,
    string? BranchName,
    string? HeadCommitSha,
    bool IsDirty,
    DateTimeOffset? BaselineObservedAtUtc);
