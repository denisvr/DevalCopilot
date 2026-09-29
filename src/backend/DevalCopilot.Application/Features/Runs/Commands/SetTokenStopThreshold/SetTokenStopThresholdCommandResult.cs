namespace DevalCopilot.Application.Features.Runs.Commands.SetTokenStopThreshold;

public sealed record SetTokenStopThresholdCommandResult(string Provider, long? ThresholdTokens);
