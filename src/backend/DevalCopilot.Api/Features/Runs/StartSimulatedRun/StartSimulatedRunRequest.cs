namespace DevalCopilot.Api.Features.Runs.StartSimulatedRun;

public sealed record StartSimulatedRunRequest(Guid ProjectId, string Objective);
