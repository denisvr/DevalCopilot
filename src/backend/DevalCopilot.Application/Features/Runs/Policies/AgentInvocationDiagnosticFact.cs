using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// ADR-0072: what an attempt's own stored facts say about its closed invocation diagnostic: <see cref="AgentInvocationDiagnosticState.Recorded"/>
/// with the category for a known category and version pair, <see cref="AgentInvocationDiagnosticState.NotRecorded"/> when neither column is
/// set (every attempt before diagnostics existed and every success), and <see cref="AgentInvocationDiagnosticState.Unknown"/> for any other
/// shape. A category is what was observed or reported, never a verified cause.
/// </summary>
public sealed record AgentInvocationDiagnosticFact(AgentInvocationDiagnosticState State, AgentInvocationDiagnostic? Category, string? Version)
{
    public static AgentInvocationDiagnosticFact ForAttempt(Attempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (attempt.AgentInvocationDiagnostic is null && attempt.AgentInvocationDiagnosticVersion is null)
        {
            return new AgentInvocationDiagnosticFact(AgentInvocationDiagnosticState.NotRecorded, null, null);
        }

        return attempt.GetKnownAgentInvocationDiagnostic() is { } category
            ? new AgentInvocationDiagnosticFact(AgentInvocationDiagnosticState.Recorded, category, attempt.AgentInvocationDiagnosticVersion)
            : new AgentInvocationDiagnosticFact(AgentInvocationDiagnosticState.Unknown, null, null);
    }
}
