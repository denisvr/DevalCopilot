namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed, neutral category of a participant in a run's durable timeline or collaboration
/// protocol. This is category only — never provider provenance and never semantic workflow role.
/// A real Agent's role and provider are carried separately via <see cref="ParticipantIdentity"/>.
/// </summary>
public enum ParticipantKind
{
    None = 0,
    Orchestrator = 1,
    Agent = 2,
    Human = 3,
}
