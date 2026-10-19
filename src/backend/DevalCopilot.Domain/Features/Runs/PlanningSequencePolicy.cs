namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one Domain-owned definition of the fixed planning progression, its deadlines and its state classification (ADR-0055). The
/// table is closed: Planner, CriticalReviewer, Resolver, CriticalReviewer, Resolver, with at most five claimed attempts.
/// </summary>
public static class PlanningSequencePolicy
{
    public const int MaximumSteps = 5;

    /// <summary>The preparation deadline, measured from the reservation of the step's one preparation intent.</summary>
    public static readonly TimeSpan PreparationDeadline = TimeSpan.FromMinutes(2);

    /// <summary>The coordination grace added to the claim time and the reserved invocation timeout. It is an observation deadline,
    /// not measured invocation time, an account budget or proof of process death.</summary>
    public static readonly TimeSpan ResultGrace = TimeSpan.FromMinutes(2);

    public static AgentRole RoleOf(int ordinal) => ordinal switch
    {
        0 => AgentRole.Planner,
        1 or 3 => AgentRole.CriticalReviewer,
        2 or 4 => AgentRole.Resolver,
        _ => throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "A planning sequence has steps 0 to 4."),
    };

    public static AgentResponseContract ContractOf(int ordinal) => RoleOf(ordinal) switch
    {
        AgentRole.Planner => AgentResponseContract.Proposal,
        AgentRole.CriticalReviewer => AgentResponseContract.CriticalReview,
        _ => AgentResponseContract.ChallengeResolution,
    };

    public static AgentProvider ProviderOf(int ordinal) => RoleOf(ordinal) == AgentRole.CriticalReviewer
        ? AgentProvider.ClaudeCode
        : AgentProvider.Codex;

    /// <summary>The coordinator may reserve or observe a step: the sequence has not ended and has not been told to stop.</summary>
    public static bool IsAdvancing(PlanningSequenceState state) =>
        state is PlanningSequenceState.Armed or PlanningSequenceState.PreparingStep or PlanningSequenceState.AwaitingStepResult;

    /// <summary>Automatic authority is still live: it can advance or it drains an already claimed step.</summary>
    public static bool IsAuthorityLive(PlanningSequenceState state) => IsAdvancing(state) || state == PlanningSequenceState.Stopping;

    public static bool IsEnded(PlanningSequenceState state) => !IsAuthorityLive(state);

    /// <summary>The preparation intent expires exactly at its recorded deadline.</summary>
    public static DateTimeOffset PreparationDeadlineOf(DateTimeOffset reservedAtUtc) => reservedAtUtc + PreparationDeadline;

    /// <summary>
    /// Claim time plus the attempt's actual reserved invocation timeout plus the grace, in checked arithmetic. Null when the timeout
    /// is not a positive bounded value or the sum is not representable: such a deadline is never fabricated.
    /// </summary>
    public static DateTimeOffset? ResultDeadlineOf(DateTimeOffset claimedAtUtc, TimeSpan? reservedInvocationTimeout)
    {
        if (reservedInvocationTimeout is not { } timeout || timeout <= TimeSpan.Zero)
        {
            return null;
        }

        try
        {
            var ticks = checked(timeout.Ticks + ResultGrace.Ticks);
            return claimedAtUtc.AddTicks(ticks);
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a result observed at <paramref name="observedAtUtc"/> is at or past the result deadline. A running attempt is observed at
    /// the clock; a terminal attempt at its own recorded completion time, so a timely completion that is only observed later stays
    /// timely. An unrepresentable deadline is never trusted, so it counts as elapsed.
    /// </summary>
    public static bool IsResultLate(DateTimeOffset claimedAtUtc, TimeSpan? reservedInvocationTimeout, DateTimeOffset observedAtUtc) =>
        ResultDeadlineOf(claimedAtUtc, reservedInvocationTimeout) is not { } deadline || observedAtUtc >= deadline;

    /// <summary>The fixed lowercase hexadecimal SHA-256 form used by the arming tuple and the objective binding.</summary>
    public static bool IsLowercaseSha256(string? value)
    {
        if (value is not { Length: 64 })
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
