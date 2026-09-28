namespace DevalCopilot.Application.Features.Runs.Commands.SetTokenWarningThreshold;

public sealed record SetTokenWarningThresholdCommandResult(string Provider, long? ThresholdTokens);
