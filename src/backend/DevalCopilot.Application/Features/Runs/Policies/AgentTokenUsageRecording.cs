using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// Translates the provider-neutral <see cref="AgentTokenUsage"/> port contract into Domain
/// <see cref="AgentTokenUsageEvidence"/> and pre-evaluates <see cref="AgentTokenUsageEvidencePolicy"/>
/// for the six Agent result-recording commands, so every violation fails closed as a stable
/// <see cref="Error"/> before any mutation. Mirrors <see cref="AgentProcessEvidenceRecording"/>;
/// shared deliberately because it is one invariant every Agent role must change together with.
/// The Domain completion transitions remain an independent backstop.
/// </summary>
public static class AgentTokenUsageRecording
{
    public const string InvalidEvidenceCode = "agent_attempts.invalid_token_usage_evidence";
    public const string EvidenceWithoutDispatchCode = "agent_attempts.token_usage_requires_dispatch";
    public const string PreInvocationOutcomeCannotCarryEvidenceCode = "agent_attempts.pre_invocation_outcome_cannot_carry_token_usage";

    /// <summary>Validates the supplied usage against the outcome the handler is about to record.
    /// On success, <paramref name="domainEvidence"/> holds the Domain value to pass to the
    /// completion transition (null when no usage was supplied).</summary>
    public static Error? Validate(
        AgentTokenUsage? usage,
        AgentProvider? provider,
        AgentOutcome requestedOutcome,
        bool dispatched,
        out AgentTokenUsageEvidence? domainEvidence)
    {
        domainEvidence = null;
        if (usage is not null)
        {
            if (AgentTokenUsageEvidence.Validate(
                    usage.InputTokens,
                    usage.OutputTokens,
                    usage.CacheCreationInputTokens,
                    usage.CacheReadInputTokens,
                    usage.SchemaVersion) is not null)
            {
                return Error.Failure(InvalidEvidenceCode, "The reported token-usage evidence is not valid.");
            }

            domainEvidence = AgentTokenUsageEvidence.Create(
                usage.InputTokens, usage.OutputTokens, usage.CacheCreationInputTokens, usage.CacheReadInputTokens, usage.SchemaVersion);
        }

        var violation = AgentTokenUsageEvidencePolicy.Evaluate(provider, requestedOutcome, dispatched, domainEvidence);
        if (violation is not null)
        {
            domainEvidence = null;
        }

        return violation switch
        {
            null => null,
            AgentTokenUsageEvidenceViolation.NotDispatched => Error.Conflict(
                EvidenceWithoutDispatchCode, "Token-usage evidence cannot be recorded for an attempt that was never dispatched."),
            AgentTokenUsageEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence => Error.Conflict(
                PreInvocationOutcomeCannotCarryEvidenceCode,
                "This outcome is always detected before the provider is ever invoked and cannot carry token-usage evidence."),
            _ => Error.Failure(InvalidEvidenceCode, "The reported token-usage evidence is not valid."),
        };
    }
}
