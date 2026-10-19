using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentSupervisions;

/// <summary>
/// The one rule that closes an open observation from a fresh durable read of its Attempt (ADR-0056), never from a method return or an
/// assumption that a result committed. A terminal Attempt closes it cleanly (judged at the Attempt's own recorded completion, so a timely
/// result observed late is not attention and a late one is); a Running Attempt records that the observer ended without a durable
/// terminal result; an observation of a host that is no longer running is closed as interrupted. Every actual closure, clean or not,
/// appends exactly one closed event atomically with the receipt change. The first attention is retained and a closure never
/// terminalizes, retries or cancels anything.
/// </summary>
internal static class AgentSupervisionClosure
{
    /// <summary>Closes the observation. False, changing nothing, when the recorded times are not coherent with the clock.</summary>
    public static bool TryClose(
        AgentSupervision receipt, Attempt? attempt, DateTimeOffset nowUtc, bool hostInterrupted, List<RunEvent> events)
    {
        if (!AgentSupervisionPolicy.IsCoherent(receipt) || nowUtc == default || nowUtc < receipt.LastHeartbeatAtUtc)
        {
            return false;
        }

        if (hostInterrupted)
        {
            AgentSupervisionWrites.RecordAttentionIfNone(receipt, AgentSupervisionAttentionReason.HostInterrupted, nowUtc, events);
            receipt.Close(AgentSupervisionClosureReason.HostInterrupted, nowUtc);
            events.Add(AgentSupervisionEvents.Closed(receipt, nowUtc));
            return true;
        }

        if (attempt is not null && AgentSupervisionAttempts.IsTerminal(attempt))
        {
            // A completion recorded after the clock is refused, never clamped: the observation stays open and unavailable.
            var judged = AgentSupervisionAttempts.AssessTerminal(receipt, attempt.CompletedAtUtc, nowUtc);
            if (!judged.ChronologyValid)
            {
                return false;
            }

            AgentSupervisionWrites.RecordFirstAttention(receipt, judged, nowUtc, events);
            receipt.Close(AgentSupervisionClosureReason.AttemptTerminal, nowUtc);
            events.Add(AgentSupervisionEvents.Closed(receipt, nowUtc));
            return true;
        }

        var assessment = AgentSupervisionPolicy.Assess(receipt, nowUtc, ownedByCurrentHost: true);
        if (!assessment.ChronologyValid)
        {
            return false;
        }

        AgentSupervisionWrites.RecordFirstAttention(receipt, assessment, nowUtc, events);
        AgentSupervisionWrites.RecordAttentionIfNone(
            receipt, AgentSupervisionAttentionReason.ObserverEndedWithoutTerminalResult, nowUtc, events);
        receipt.Close(AgentSupervisionClosureReason.ObserverEnded, nowUtc);
        events.Add(AgentSupervisionEvents.Closed(receipt, nowUtc));
        return true;
    }
}
