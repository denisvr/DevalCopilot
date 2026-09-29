using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The claim-time token-activity stop shared by all six Agent-claiming handlers, each with its own
/// fixed provider. With no threshold configured for that provider it does nothing and reads nothing,
/// so existing claim behavior is unchanged. Otherwise it derives the provider's evaluation from the
/// run's persisted dispatched Agent attempts and refuses the claim when the count reached the
/// threshold or staying below it cannot be proved. It runs before any provider-availability probe,
/// Git work, manifest sealing, or review-correction authorization consumption, after (never instead
/// of) the run-wide count and reserved-time budgets. This is a retrospective local guardrail: not an
/// account allowance, a per-attempt cap, or a token reservation.
///
/// <para>
/// Concluded attempts' usage is written by the same transition that concludes them and never
/// changes afterwards, and any attempt committed by a concurrent claim collides on the run's Agent
/// budget slot, so the only input that can change between this check and a claim's commit is the
/// threshold itself; <see cref="CurrentTokenStopPolicy"/> guards that at the durable claim boundary.
/// </para>
/// </summary>
public static class AgentTokenStopGate
{
    public const string ReachedCode = "agent_attempts.token_stop_reached";
    public const string EvidenceIndeterminateCode = "agent_attempts.token_stop_evidence_indeterminate";

    /// <summary>The configured stop threshold this run has for <paramref name="provider"/>, or null.</summary>
    public static long? ThresholdFor(Run run, AgentProvider provider) => provider switch
    {
        AgentProvider.Codex => run.CodexTokenStopThreshold,
        AgentProvider.ClaudeCode => run.ClaudeTokenStopThreshold,
        _ => null,
    };

    /// <summary>Returns the refusal for a claim on <paramref name="provider"/>, or null when the stop
    /// permits it (or none is configured).</summary>
    public static async Task<Error?> CheckClaimAsync(
        IDevalCopilotDbContext dbContext, Run run, AgentProvider provider, CancellationToken cancellationToken)
    {
        var threshold = ThresholdFor(run, provider);
        if (threshold is null)
        {
            return null;
        }

        var accumulator = new AgentTokenStopAccumulator();
        var dispatchedAttempts = dbContext.Attempts
            .AsNoTracking()
            .Where(attempt => attempt.RunId == run.Id && attempt.Kind == AttemptKind.Agent && attempt.AgentDispatchedAtUtc != null)
            .Select(PersistedAgentAttemptStopEvidence.Projection)
            .AsAsyncEnumerable();

        await foreach (var attempt in dispatchedAttempts.WithCancellation(cancellationToken))
        {
            var (status, attemptProvider) = attempt.Classify();
            accumulator.Add(status, attemptProvider, attempt.Usage(attemptProvider));
        }

        return ToError(accumulator.ToEvaluation(provider, threshold));
    }

    /// <summary>The refusal for a blocking evaluation; null when it permits a claim.</summary>
    public static Error? ToError(AgentTokenStopEvaluation evaluation) => evaluation.State switch
    {
        AgentTokenStopState.ThresholdReached => Error.Conflict(
            ReachedCode,
            "This run's locally recorded token activity for this provider has reached its configured stop threshold."),
        AgentTokenStopState.EvidenceIndeterminate => Error.Failure(
            EvidenceIndeterminateCode,
            "This run's locally recorded token activity for this provider cannot be proved to be below its configured stop threshold: relevant usage evidence is pending, missing, malformed, unsupported, unattributed, or not representable."),
        _ => null,
    };
}
