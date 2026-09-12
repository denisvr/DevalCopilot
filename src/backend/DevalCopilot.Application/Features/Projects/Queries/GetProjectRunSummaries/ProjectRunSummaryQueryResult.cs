using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectRunSummaries;

public sealed record ProjectRunSummaryQueryResult(
    Guid ProjectId,
    string ProjectName,
    Guid? RunId,
    int? ExecutionNumber,
    RunLifecycle? Lifecycle,
    RunStage? Stage);
