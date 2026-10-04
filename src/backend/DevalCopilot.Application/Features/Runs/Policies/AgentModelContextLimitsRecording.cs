using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// Translates the provider-neutral <see cref="AgentModelContextLimits"/> port contract into Domain
/// <see cref="AgentModelContextLimitsEvidence"/> and pre-evaluates <see cref="AgentModelContextLimitsEvidencePolicy"/> for
/// the three Claude result-recording commands, so every violation fails closed as a stable <see cref="Error"/> before any
/// mutation. Mirrors <see cref="AgentTokenUsageRecording"/>; shared deliberately because it is one invariant every Claude
/// role must change together with. The Domain completion transitions remain an independent backstop.
/// </summary>
public static class AgentModelContextLimitsRecording
{
    public const string InvalidEvidenceCode = "agent_attempts.invalid_model_context_limits_evidence";
    public const string EvidenceWithoutDispatchCode = "agent_attempts.model_context_limits_require_dispatch";
    private const string InvalidEvidenceMessage = "The reported model context-limit evidence is not valid.";
    public const string PreInvocationOutcomeCannotCarryEvidenceCode =
        "agent_attempts.pre_invocation_outcome_cannot_carry_model_context_limits";

    /// <summary>Validates the supplied limits against the outcome the handler is about to record. On success,
    /// <paramref name="domainEvidence"/> holds the Domain value to pass to the completion transition (null when no limits
    /// were supplied).</summary>
    public static Error? Validate(
        AgentModelContextLimits? limits,
        AgentProvider? provider,
        AgentOutcome requestedOutcome,
        bool dispatched,
        out AgentModelContextLimitsEvidence? domainEvidence)
    {
        domainEvidence = null;
        if (limits is not null)
        {
            // The value already holds a bounded snapshot, so this judges the count before touching any entry, then refuses a null
            // entry, then lets the one Domain rule judge the shape. Nothing is caught: malformed input is refused by a check.
            var entries = limits.Models;
            if (entries.IsDefaultOrEmpty || entries.Length > AgentModelContextLimitsEvidence.MaxModels || entries.Any(entry => entry is null))
            {
                return Error.Failure(InvalidEvidenceCode, InvalidEvidenceMessage);
            }

            var models = new AgentModelContextLimit[entries.Length];
            for (var index = 0; index < models.Length; index++)
            {
                var entry = entries[index]!;
                models[index] = new AgentModelContextLimit(entry.ModelId, entry.ContextWindowTokens, entry.MaxOutputTokens);
            }

            if (AgentModelContextLimitsEvidence.Validate(limits.Source, models) is not null)
            {
                return Error.Failure(InvalidEvidenceCode, InvalidEvidenceMessage);
            }

            domainEvidence = AgentModelContextLimitsEvidence.Create(limits.Source, models);
        }

        var violation = AgentModelContextLimitsEvidencePolicy.Evaluate(provider, requestedOutcome, dispatched, domainEvidence);
        if (violation is not null)
        {
            domainEvidence = null;
        }

        return violation switch
        {
            null => null,
            AgentModelContextLimitsEvidenceViolation.NotDispatched => Error.Conflict(
                EvidenceWithoutDispatchCode,
                "Model context-limit evidence cannot be recorded for an attempt that was never dispatched."),
            AgentModelContextLimitsEvidenceViolation.PreInvocationOutcomeCannotCarryEvidence => Error.Conflict(
                PreInvocationOutcomeCannotCarryEvidenceCode,
                "This outcome is always detected before the provider is ever invoked and cannot carry model context-limit evidence."),
            _ => Error.Failure(InvalidEvidenceCode, InvalidEvidenceMessage),
        };
    }
}
