using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectRunSummaries;

/// <param name="Capabilities">The same shared host observation, projected for this project's
/// default requirement classification — identical across every project, since the underlying
/// evidence is host-scoped, not project-scoped.</param>
public sealed record ProjectRunSummaryQueryResult(
    Guid ProjectId,
    string ProjectName,
    Guid? RunId,
    int? ExecutionNumber,
    RunLifecycle? Lifecycle,
    RunStage? Stage,
    IReadOnlyList<CapabilityReadinessQueryResult> Capabilities);
