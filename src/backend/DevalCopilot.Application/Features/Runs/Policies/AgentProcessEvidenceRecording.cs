using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// Translates the provider-neutral <see cref="AgentProcessEvidence"/> port contract into Domain
/// <see cref="AgentProcessExecutionEvidence"/> and pre-evaluates
/// <see cref="AgentProcessEvidencePolicy"/> for the six Agent result-recording commands, so every
/// violation fails closed as a stable <see cref="Error"/> before any mutation. Shared deliberately:
/// it is one invariant that every Agent role must change together with, while each handler keeps
/// its own role-specific validation. The Domain completion transitions remain an independent
/// backstop.
/// </summary>
public static class AgentProcessEvidenceRecording
{
    public const string InvalidEvidenceCode = "agent_attempts.invalid_process_evidence";
    public const string EvidenceWithoutDispatchCode = "agent_attempts.process_evidence_requires_dispatch";
    public const string CleanExitRequiredCode = "agent_attempts.outcome_requires_clean_process_exit";
    public const string PreInvocationOutcomeCannotCarryEvidenceCode = "agent_attempts.pre_invocation_outcome_cannot_carry_process_evidence";

    /// <summary>Validates the supplied evidence against the outcome the handler is about to record.
    /// On success, <paramref name="domainEvidence"/> holds the Domain value to pass to the
    /// completion transition (null when no evidence was supplied).</summary>
    public static Error? Validate(
        AgentProcessEvidence? evidence,
        AgentOutcome requestedOutcome,
        bool dispatched,
        out AgentProcessExecutionEvidence? domainEvidence)
    {
        domainEvidence = null;
        if (evidence is not null)
        {
            var shapeError = TryConvert(evidence, out domainEvidence);
            if (shapeError is not null)
            {
                return shapeError;
            }
        }

        return AgentProcessEvidencePolicy.Evaluate(requestedOutcome, dispatched, domainEvidence) switch
        {
            null => null,
            AgentProcessEvidenceViolation.NotDispatched => Error.Conflict(
                EvidenceWithoutDispatchCode, "Process evidence cannot be recorded for an attempt that was never dispatched."),
            AgentProcessEvidenceViolation.CleanExitRequired => Error.Failure(
                CleanExitRequiredCode, "This outcome requires evidence that the provider process exited with code zero."),
            AgentProcessEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence => Error.Conflict(
                PreInvocationOutcomeCannotCarryEvidenceCode,
                "This outcome is always detected before the provider is ever invoked and cannot carry process evidence."),
            _ => Error.Failure(InvalidEvidenceCode, "The reported process evidence is not valid."),
        };
    }

    private static Error? TryConvert(AgentProcessEvidence evidence, out AgentProcessExecutionEvidence? domainEvidence)
    {
        domainEvidence = null;
        ProcessOutcome? outcome = evidence.Outcome switch
        {
            ProcessExecutionOutcome.Exited => ProcessOutcome.Exited,
            ProcessExecutionOutcome.TimedOut => ProcessOutcome.TimedOut,
            ProcessExecutionOutcome.Cancelled => ProcessOutcome.Cancelled,
            _ => null,
        };

        if (outcome is not { } mappedOutcome
            || AgentProcessExecutionEvidence.Validate(mappedOutcome, evidence.ExitCode, evidence.Duration) is not null)
        {
            return Error.Failure(InvalidEvidenceCode, "The reported process evidence is not valid.");
        }

        domainEvidence = AgentProcessExecutionEvidence.Create(mappedOutcome, evidence.ExitCode, evidence.Duration);
        return null;
    }
}
