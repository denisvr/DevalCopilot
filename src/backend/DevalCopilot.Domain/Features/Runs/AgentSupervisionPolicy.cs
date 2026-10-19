namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The one Domain-owned definition of Agent supervision timing and attention (ADR-0056): a five-second heartbeat, staleness at thirty
/// seconds, two minutes for Preparing and RecordingResult, and the attempt's actual reserved invocation timeout plus two minutes for
/// AwaitingAdapterResult. Every deadline is fixed at phase entry in checked arithmetic and an exact boundary is expired. A heartbeat
/// never extends a phase deadline, so a healthy heartbeat can never mask a hung phase. None of it is process liveness, provider progress
/// or execution authority.
/// </summary>
public static class AgentSupervisionPolicy
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan PreparingDeadline = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan RecordingResultDeadline = TimeSpan.FromMinutes(2);

    /// <summary>The grace added to the reserved invocation timeout. It is an observation deadline, not measured invocation time.</summary>
    public static readonly TimeSpan AdapterResultGrace = TimeSpan.FromMinutes(2);

    /// <summary>Every observation call and every disposal wait is bounded to this, independently of the underlying operation.</summary>
    public static readonly TimeSpan CallBound = TimeSpan.FromSeconds(2);

    /// <summary>The bounded inventory one sweep round visits; the cursor makes every open observation visited eventually.</summary>
    public const int SweepBatchSize = 16;

    public static int Rank(AgentSupervisionPhase phase) => phase switch
    {
        AgentSupervisionPhase.Preparing => 0,
        AgentSupervisionPhase.AwaitingAdapterResult => 1,
        AgentSupervisionPhase.RecordingResult => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Not an Agent supervision phase."),
    };

    /// <summary>
    /// The absolute deadline of a phase entered at <paramref name="enteredAtUtc"/>. AwaitingAdapterResult needs the attempt's positive
    /// reserved invocation timeout. Null when the timeout is missing or not positive or the sum is not representable: such a deadline is
    /// never fabricated.
    /// </summary>
    public static DateTimeOffset? PhaseDeadlineOf(
        AgentSupervisionPhase phase, DateTimeOffset enteredAtUtc, TimeSpan? reservedInvocationTimeout)
    {
        TimeSpan span;
        switch (phase)
        {
            case AgentSupervisionPhase.Preparing:
                span = PreparingDeadline;
                break;
            case AgentSupervisionPhase.RecordingResult:
                span = RecordingResultDeadline;
                break;
            case AgentSupervisionPhase.AwaitingAdapterResult:
                if (reservedInvocationTimeout is not { } timeout || timeout <= TimeSpan.Zero)
                {
                    return null;
                }

                try
                {
                    span = TimeSpan.FromTicks(checked(timeout.Ticks + AdapterResultGrace.Ticks));
                }
                catch (OverflowException)
                {
                    return null;
                }

                break;
            default:
                return null;
        }

        return Add(enteredAtUtc, span);
    }

    /// <summary>The instant at which a heartbeat committed at <paramref name="lastHeartbeatAtUtc"/> is stale (exactly thirty seconds).</summary>
    public static DateTimeOffset? StaleAtOf(DateTimeOffset lastHeartbeatAtUtc) => Add(lastHeartbeatAtUtc, StaleAfter);

    /// <summary>Whether the stored times are internally ordered and representable, independently of any clock.</summary>
    public static bool IsCoherent(AgentSupervision receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!Enum.IsDefined(receipt.Phase)
            || receipt.StartedAtUtc > receipt.PhaseEnteredAtUtc
            || receipt.PhaseEnteredAtUtc > receipt.LastHeartbeatAtUtc
            || receipt.PhaseDeadlineUtc <= receipt.PhaseEnteredAtUtc
            || StaleAtOf(receipt.LastHeartbeatAtUtc) is null)
        {
            return false;
        }

        if ((receipt.AttentionReason is null) != (receipt.AttentionAtUtc is null)
            || (receipt.AttentionReason is not null && !AgentSupervisionAttentionReason.IsKnown(receipt.AttentionReason))
            || (receipt.AttentionAtUtc is { } attentionAt && attentionAt < receipt.StartedAtUtc))
        {
            return false;
        }

        if ((receipt.ClosureReason is null) != (receipt.ClosedAtUtc is null)
            || (receipt.ClosureReason is not null && !AgentSupervisionClosureReason.IsKnown(receipt.ClosureReason))
            || (receipt.ClosedAtUtc is { } closedAt && closedAt < receipt.LastHeartbeatAtUtc))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Judges an observation at <paramref name="nowUtc"/>. No recorded fact may lie in the future of the clock: an open observation's
    /// last report, a closed one's closure and a recorded first attention later than now are impossible (for example after a clock
    /// rollback) and make the observation unavailable, never clamped. The recorded first attention always wins; otherwise an open
    /// observation of a host that is not the running host is host-interrupted, and the earlier of the phase deadline and the staleness
    /// boundary becomes the effective attention (the deadline wins a tie).
    /// </summary>
    public static AgentSupervisionAssessment Assess(AgentSupervision receipt, DateTimeOffset nowUtc, bool ownedByCurrentHost)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!IsCoherent(receipt) || nowUtc == default)
        {
            return AgentSupervisionAssessment.Unavailable;
        }

        if (receipt.IsOpen ? nowUtc < receipt.LastHeartbeatAtUtc : nowUtc < receipt.ClosedAtUtc)
        {
            return AgentSupervisionAssessment.Unavailable;
        }

        if (receipt.AttentionAtUtc is { } attentionAt && nowUtc < attentionAt)
        {
            return AgentSupervisionAssessment.Unavailable;
        }

        if (receipt.AttentionReason is { } recorded)
        {
            return new AgentSupervisionAssessment(true, recorded, receipt.AttentionAtUtc, true);
        }

        if (!receipt.IsOpen)
        {
            return new AgentSupervisionAssessment(true, null, null, false);
        }

        return Derive(receipt, nowUtc, ownedByCurrentHost);
    }

    /// <summary>
    /// Judges what the observation looked like at <paramref name="atUtc"/>, for an Attempt whose terminal result was recorded at that
    /// instant: a timely completion that is observed long afterwards stays timely, and a completion at or after the deadline is late.
    /// A heartbeat committed after <paramref name="atUtc"/> proves the observer was reporting then. Never fresh for an instant before
    /// the observation started.
    /// </summary>
    public static AgentSupervisionAssessment AssessAt(AgentSupervision receipt, DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!IsCoherent(receipt) || atUtc == default || atUtc < receipt.StartedAtUtc)
        {
            return AgentSupervisionAssessment.Unavailable;
        }

        if (receipt.AttentionReason is { } recorded)
        {
            return new AgentSupervisionAssessment(true, recorded, receipt.AttentionAtUtc, true);
        }

        return Derive(receipt, atUtc, ownedByCurrentHost: true);
    }

    private static AgentSupervisionAssessment Derive(AgentSupervision receipt, DateTimeOffset atUtc, bool ownedByCurrentHost)
    {
        // An old host's freshness is never accepted: whatever its last heartbeat said, the host that reported it is gone.
        if (!ownedByCurrentHost)
        {
            return new AgentSupervisionAssessment(true, AgentSupervisionAttentionReason.HostInterrupted, atUtc, false);
        }

        string? reason = null;
        DateTimeOffset? at = null;
        if (atUtc >= receipt.PhaseDeadlineUtc)
        {
            reason = AgentSupervisionAttentionReason.PhaseDeadlineElapsed;
            at = receipt.PhaseDeadlineUtc;
        }

        var staleAt = StaleAtOf(receipt.LastHeartbeatAtUtc)!.Value;
        if (atUtc >= staleAt && (at is null || staleAt < at))
        {
            reason = AgentSupervisionAttentionReason.StaleHeartbeat;
            at = staleAt;
        }

        return new AgentSupervisionAssessment(true, reason, at, false);
    }

    private static DateTimeOffset? Add(DateTimeOffset value, TimeSpan span)
    {
        try
        {
            return value.AddTicks(span.Ticks);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
