using DevalCopilot.Application.Features.Runs.Policies;

namespace DevalCopilot.Api.Features.Runs;

/// <summary>
/// ADR-0072: the closed invocation diagnostic of one Agent attempt. <c>State</c> is <c>NotRecorded</c>, <c>Recorded</c> or
/// <c>Unknown</c>; <c>Category</c> and <c>Version</c> are present only when recorded. Never carries provider text, payload,
/// exception, environment value, credential, account identifier or path, and a category is never a verified cause.
/// </summary>
public sealed record AgentInvocationDiagnosticResponse(string State, string? Category, string? Version)
{
    public static AgentInvocationDiagnosticResponse? FromFact(AgentInvocationDiagnosticFact? fact) =>
        fact is null ? null : new(fact.State.ToString(), fact.Category?.ToString(), fact.Version);
}
