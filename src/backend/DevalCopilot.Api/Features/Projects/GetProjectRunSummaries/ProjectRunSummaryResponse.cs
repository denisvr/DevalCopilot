namespace DevalCopilot.Api.Features.Projects.GetProjectRunSummaries;

public sealed record ProjectRunSummaryResponse(
    Guid ProjectId,
    string ProjectName,
    Guid? RunId,
    int? ExecutionNumber,
    string? Lifecycle,
    string? Stage,
    IReadOnlyList<CapabilityReadinessResponse> Capabilities);
