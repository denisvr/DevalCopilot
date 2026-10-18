using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>The refusal both recorded-verification operations deliberately share (ADR-0052).</summary>
public static class AgentAttemptVerificationErrors
{
    public const string NotFoundCode = "agent_attempt_verification.not_found";

    /// <summary>One fixed answer for an unknown run, a missing, foreign or non-Agent attempt and, for a window, an execution that is not a
    /// proved member or a stream that is not supported, so none is distinguishable and no rejected value is repeated.</summary>
    public static Error NotFound() =>
        Error.NotFound(NotFoundCode, "The requested Agent attempt verification evidence was not found.");
}
