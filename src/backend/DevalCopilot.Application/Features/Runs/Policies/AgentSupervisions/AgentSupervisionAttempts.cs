using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;

/// <summary>The coherence judgment of an Attempt as an observable Agent attempt (ADR-0056): one of the seven existing response contracts
/// with its own role and a positive reserved invocation timeout. Simulated and Process attempts are never observed.</summary>
internal static class AgentSupervisionAttempts
{
    public static bool IsCoherentAgent(Attempt attempt) => attempt.Kind == AttemptKind.Agent
        && IsCoherentAgent(attempt.AgentProvider, attempt.AgentRole, attempt.AgentResponseContract, attempt.AgentTimeout);

    public static bool IsCoherentAgent(
        AgentProvider? provider, AgentRole? role, AgentResponseContract? contract, TimeSpan? reservedInvocationTimeout)
    {
        if (provider is not { } knownProvider
            || !Enum.IsDefined(knownProvider)
            || role is not { } knownRole
            || contract is not { } knownContract
            || !Enum.IsDefined(knownContract)
            || reservedInvocationTimeout is not { } timeout
            || timeout <= TimeSpan.Zero)
        {
            return false;
        }

        return AgentAttemptContract.For(knownContract).Role == knownRole;
    }

    public static bool IsObservable(Attempt attempt) => attempt.Status == AttemptStatus.Running && IsCoherentAgent(attempt);

    public static bool IsTerminal(Attempt attempt) => attempt.Status != AttemptStatus.Running;

    /// <summary>The instant to judge a terminal Attempt's observation at: its own recorded completion, so a timely result that is only
    /// read long afterwards stays timely (an attempt without a recorded completion is judged now). Null when the recorded completion lies
    /// after <paramref name="nowUtc"/>: that is an impossible fact (for example after a clock rollback), refused and never clamped to now.
    /// A completion exactly at now is valid.</summary>
    public static DateTimeOffset? JudgedAt(DateTimeOffset? completedAtUtc, DateTimeOffset nowUtc) => completedAtUtc switch
    {
        null => nowUtc,
        { } completed when completed <= nowUtc => completed,
        _ => null,
    };

    /// <summary>The terminal judgment of an observation: first the current chronology at <paramref name="nowUtc"/> (no recorded report,
    /// closure, first attention or completion may lie after it), then the observation as it was at the recorded completion. Unavailable
    /// whenever either is impossible.</summary>
    public static AgentSupervisionAssessment AssessTerminal(AgentSupervision receipt, DateTimeOffset? completedAtUtc, DateTimeOffset nowUtc)
    {
        if (!AgentSupervisionPolicy.Assess(receipt, nowUtc, ownedByCurrentHost: true).ChronologyValid
            || JudgedAt(completedAtUtc, nowUtc) is not { } judgedAt)
        {
            return AgentSupervisionAssessment.Unavailable;
        }

        return AgentSupervisionPolicy.AssessAt(receipt, judgedAt);
    }
}
