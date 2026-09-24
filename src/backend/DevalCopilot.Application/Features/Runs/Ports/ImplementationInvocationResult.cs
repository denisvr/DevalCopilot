namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// The outcome of one Claude implementation invocation attempt. Never carries raw stdout/stderr
/// or exception text or credentials — those are sealed separately as artifacts by the caller,
/// which already owns the sink paths the adapter wrote (or failed to write) into.
/// </summary>
public sealed record ImplementationInvocationResult(
    ImplementationInvocationOutcome Outcome,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    string? ProviderSessionId,
    string? ObservedModel = null,
    string? ObservedEffort = null,
    AgentProcessEvidence? ProcessEvidence = null);
