using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>ADR-0072: what one Claude invocation proved. <see cref="StructuredOutputJson"/> is set only for semantic success,
/// <see cref="Diagnostic"/> only for failure; usage and model limits only from a structurally valid envelope of a zero exit with
/// complete I/O.</summary>
internal sealed record ClaudeInvocationReading(
    string? StructuredOutputJson,
    AgentInvocationDiagnostic? Diagnostic,
    string? SessionId,
    AgentTokenUsage? TokenUsage,
    AgentModelContextLimits? ModelContextLimits)
{
    public bool IsSuccess => StructuredOutputJson is not null;

    public static ClaudeInvocationReading Failure(
        AgentInvocationDiagnostic diagnostic, AgentTokenUsage? usage = null, AgentModelContextLimits? limits = null) =>
        new(null, diagnostic, null, usage, limits);

    public static ClaudeInvocationReading Succeeded(
        string structuredOutputJson, string? sessionId, AgentTokenUsage? usage, AgentModelContextLimits? limits) =>
        new(structuredOutputJson, null, sessionId, usage, limits);
}
