namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>The outcome of one diagnosis invocation. Never carries raw stdout/stderr, exception text, or credentials — those
/// are sealed separately as artifacts by the caller, which owns the sink paths.</summary>
public sealed record VerificationDiagnosisInvocationResult(
    VerificationDiagnosisInvocationOutcome Outcome,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    string? ProviderSessionId,
    AgentProcessEvidence? ProcessEvidence = null,
    AgentTokenUsage? TokenUsage = null);
