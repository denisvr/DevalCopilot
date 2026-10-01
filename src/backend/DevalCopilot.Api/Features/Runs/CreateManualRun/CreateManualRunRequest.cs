namespace DevalCopilot.Api.Features.Runs.CreateManualRun;

public sealed record CreateManualRunRequest(Guid ProjectId, string Objective);
