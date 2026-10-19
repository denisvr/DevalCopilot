namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The fixed reason codes that closed an Agent supervision observation (ADR-0056). A closure never terminalizes the Attempt: it only says
/// the host stopped observing. The text is durable schema.
/// </summary>
public static class AgentSupervisionClosureReason
{
    /// <summary>A fresh durable read showed the Attempt already terminal.</summary>
    public const string AttemptTerminal = "attempt_terminal";

    /// <summary>The observer returned while a fresh durable read still showed the Attempt Running.</summary>
    public const string ObserverEnded = "observer_ended";

    /// <summary>Startup (or the sweep) closed the observation of a host that no longer runs.</summary>
    public const string HostInterrupted = "host_interrupted";

    public static bool IsKnown(string? reason) => reason is AttemptTerminal or ObserverEnded or HostInterrupted;
}
