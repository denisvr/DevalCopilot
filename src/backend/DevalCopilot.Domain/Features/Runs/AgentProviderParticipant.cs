namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed, provider-only mapping from an Agent attempt's real provider to the participant
/// value that truthfully records it as provenance. This mapping owns provenance only — it grants
/// no authorization and is never consulted to decide which collaboration message type a role may
/// author; see <see cref="CollaborationMessageAuthorPolicy"/> for that, which has no provider
/// dimension at all.
/// </summary>
public static class AgentProviderParticipant
{
    public static ParticipantKind For(AgentProvider provider) => provider switch
    {
        AgentProvider.Codex => ParticipantKind.Codex,
        AgentProvider.ClaudeCode => ParticipantKind.Claude,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "No participant is defined for this provider."),
    };
}
