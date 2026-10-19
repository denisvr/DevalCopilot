namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputMessage;

/// <summary>The closed state of one selected output message. <see cref="Unavailable"/> covers an unprovable attempt identity, an output
/// set larger than this inspection reads, an incoherent association and a reread message whose association, header, provenance or actor
/// disagrees with its admitted reference; it discloses neither which nor any part of the set. <see cref="Recorded"/> is a proved member
/// that was reread unchanged; its content may still be unavailable.</summary>
public enum AgentAttemptOutputMessageStatus
{
    Unavailable,
    Recorded,
}
