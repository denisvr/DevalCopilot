namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputMessage;

/// <summary>The closed state of one selected input message. <see cref="Unavailable"/> covers an unprovable attempt identity, an input
/// set larger than this inspection reads, an incoherent set and a reread message whose header disagrees with its admitted reference; it
/// discloses neither which nor any part of the set. <see cref="Recorded"/> is a proved member whose header was reread unchanged; its
/// content may still be unavailable.</summary>
public enum AgentAttemptInputMessageStatus
{
    Unavailable,
    Recorded,
}
