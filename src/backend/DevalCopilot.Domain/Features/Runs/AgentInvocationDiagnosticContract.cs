namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// ADR-0072: the version of the closed <see cref="AgentInvocationDiagnostic"/> vocabulary recorded beside each category, and the
/// one rule for which attempts may carry one: a dispatched Claude Agent attempt whose recorded outcome is a failure, never a
/// semantic success. The category is write-once and stored atomically with the failed result.
/// </summary>
public static class AgentInvocationDiagnosticContract
{
    public const string CurrentVersion = "agent-invocation-diagnostic-v1";

    /// <summary>Whether <paramref name="version"/> and <paramref name="diagnostic"/> are a known, defined pair.</summary>
    public static bool IsKnown(string? version, AgentInvocationDiagnostic? diagnostic) =>
        version == CurrentVersion && diagnostic is { } value && Enum.IsDefined(value);
}
