using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Ports;

public sealed record ReviewCorrectionInvocationResult(
    ImplementationInvocationOutcome Outcome,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    string? ProviderSessionId,
    AgentProcessEvidence? ProcessEvidence = null,
    AgentTokenUsage? TokenUsage = null,
    AgentModelContextLimits? ModelContextLimits = null,
    AgentInvocationDiagnostic? Diagnostic = null);
