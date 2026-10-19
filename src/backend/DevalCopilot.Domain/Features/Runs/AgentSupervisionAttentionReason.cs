namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The fixed reason codes of the first attention of an Agent supervision observation (ADR-0056). The text is durable schema. Attention
/// proves only that the host's own observation missed a deadline or ended; it never proves that earlier authorized work stopped.
/// </summary>
public static class AgentSupervisionAttentionReason
{
    /// <summary>No heartbeat was committed for at least thirty seconds.</summary>
    public const string StaleHeartbeat = "stale_heartbeat";

    /// <summary>The current phase's absolute deadline elapsed, whatever the heartbeat reported.</summary>
    public const string PhaseDeadlineElapsed = "phase_deadline_elapsed";

    /// <summary>The observer returned while the Attempt had no durable terminal result.</summary>
    public const string ObserverEndedWithoutTerminalResult = "observer_ended_without_terminal_result";

    /// <summary>The host that owned the observation is no longer the running host.</summary>
    public const string HostInterrupted = "host_interrupted";

    public static bool IsKnown(string? reason) =>
        reason is StaleHeartbeat or PhaseDeadlineElapsed or ObserverEndedWithoutTerminalResult or HostInterrupted;
}
