namespace DevalCopilot.Api.Features.Runs.SetTokenWarningThreshold;

public sealed record SetTokenWarningThresholdResponse(string Provider, long? ThresholdTokens);
