namespace DevalCopilot.Api.Features.Runs.SetTokenStopThreshold;

public sealed record SetTokenStopThresholdResponse(string Provider, long? ThresholdTokens);
