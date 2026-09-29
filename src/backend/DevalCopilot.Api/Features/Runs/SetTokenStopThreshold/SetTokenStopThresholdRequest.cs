namespace DevalCopilot.Api.Features.Runs.SetTokenStopThreshold;

public sealed record SetTokenStopThresholdRequest(string Provider, long? ThresholdTokens);
