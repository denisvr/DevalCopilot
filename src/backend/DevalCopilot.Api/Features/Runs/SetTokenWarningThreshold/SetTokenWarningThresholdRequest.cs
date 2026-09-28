namespace DevalCopilot.Api.Features.Runs.SetTokenWarningThreshold;

public sealed record SetTokenWarningThresholdRequest(string Provider, long? ThresholdTokens);
