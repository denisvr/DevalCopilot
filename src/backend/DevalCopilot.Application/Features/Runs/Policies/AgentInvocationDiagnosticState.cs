namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>ADR-0072: whether an attempt recorded a known closed invocation diagnostic.</summary>
public enum AgentInvocationDiagnosticState
{
    NotRecorded = 0,
    Recorded = 1,
    Unknown = 2,
}
