using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// ADR-0072: pre-evaluates, before any mutation, whether the three Claude result-recording commands may record a closed invocation
/// diagnostic beside the outcome they are about to record: only for a dispatched Claude attempt, only beside a failure that is not
/// a pre-invocation classification, and never beside the contract's semantic success. The Domain transition
/// (<see cref="Attempt.RecordAgentInvocationDiagnostic"/>) and the completion transitions remain the independent backstop.
/// </summary>
public static class AgentInvocationDiagnosticRecording
{
    public const string InvalidDiagnosticCode = "agent_attempts.invalid_invocation_diagnostic";

    public static Error? Validate(AgentInvocationDiagnostic? diagnostic, Attempt attempt, AgentOutcome outcome)
    {
        if (diagnostic is not { } value)
        {
            return null;
        }

        var isSuccess = attempt.AgentResponseContract is { } contract
            && AgentAttemptContract.For(contract).CompletedOutcomes.Contains(outcome);
        if (!Enum.IsDefined(value)
            || attempt.AgentProvider != AgentProvider.ClaudeCode
            || !attempt.AgentDispatchedAtUtc.HasValue
            || isSuccess
            || AgentProcessEvidencePolicy.IsPreInvocationOutcome(outcome))
        {
            return Error.Failure(InvalidDiagnosticCode, "The invocation diagnostic does not fit the recorded outcome.");
        }

        return null;
    }
}
