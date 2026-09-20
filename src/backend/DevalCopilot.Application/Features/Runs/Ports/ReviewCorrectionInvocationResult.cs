namespace DevalCopilot.Application.Features.Runs.Ports;

public sealed record ReviewCorrectionInvocationResult(
    ImplementationInvocationOutcome Outcome,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    string? ProviderSessionId);
