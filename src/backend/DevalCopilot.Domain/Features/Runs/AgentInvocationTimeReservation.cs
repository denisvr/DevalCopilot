namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The pure computation behind the run-wide Agent invocation-time budget (see the companion ADR
/// to ADR-0012): the total time a run's already-claimed Agent attempts have permanently reserved.
/// Kept as one small, dependency-free Domain helper so every caller — every Agent-claiming command
/// handler and the run cockpit projection — evaluates the exact same fail-closed rule, rather than
/// each re-deriving its own summation and its own tolerance for missing evidence.
/// </summary>
public static class AgentInvocationTimeReservation
{
    /// <summary>
    /// Sums the <see cref="Attempt.AgentTimeout"/> already reserved by a run's claimed Agent
    /// attempts. Fails closed — returns <see langword="null"/> — the instant any one of the given
    /// values is missing or not a positive, bounded timeout: a persisted Agent attempt is never
    /// silently treated as reserving zero time merely because its own evidence is absent or
    /// malformed, since that would understate what the run has actually committed to. Callers pass
    /// exactly the <see cref="Attempt.AgentTimeout"/> values of this run's own Agent-kind attempts
    /// (a Simulated or Process attempt is never included; it never consumes this budget).
    /// </summary>
    public static TimeSpan? ComputeReserved(IEnumerable<TimeSpan?> claimedAgentAttemptTimeouts)
    {
        ArgumentNullException.ThrowIfNull(claimedAgentAttemptTimeouts);

        var totalTicks = 0L;
        foreach (var timeout in claimedAgentAttemptTimeouts)
        {
            if (timeout is not { } value || value <= TimeSpan.Zero)
            {
                return null;
            }

            try
            {
                totalTicks = checked(totalTicks + value.Ticks);
            }
            catch (OverflowException)
            {
                // A positive but unrepresentable aggregate is evidence that cannot be trusted, not
                // an actual reservation of more time than a TimeSpan can express: fail closed the
                // same way missing or non-positive evidence does, rather than letting the overflow
                // propagate as an unhandled exception.
                return null;
            }
        }

        return TimeSpan.FromTicks(totalTicks);
    }

    /// <summary>
    /// Adds one candidate Agent attempt's own configured timeout to an already-reserved total,
    /// failing closed — returning <see langword="null"/> — rather than throwing when the sum is
    /// not representable as a <see cref="TimeSpan"/>. Callers treat a <see langword="null"/> result
    /// exactly like malformed prior evidence (<c>agent_attempts.time_budget_evidence_invalid</c>),
    /// never as a claim that happens to fit.
    /// </summary>
    public static TimeSpan? ComputeProjectedReservation(TimeSpan alreadyReserved, TimeSpan candidateTimeout)
    {
        try
        {
            return TimeSpan.FromTicks(checked(alreadyReserved.Ticks + candidateTimeout.Ticks));
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
