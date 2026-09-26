namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// The provider bucket a dispatched Agent attempt's token-usage evidence is attributed to in the
/// cockpit's provider-separated token-usage projection. <see cref="Unattributed"/> is the fail-closed
/// bucket for a dispatched Agent attempt whose recorded <see cref="AgentProvider"/> is null or not one
/// of the two known providers below — a dispatched attempt is always counted in exactly one bucket
/// here, never silently dropped and never guessed into the wrong provider.
/// </summary>
public enum RunCockpitProviderTokenUsageAttribution
{
    Codex = 0,
    ClaudeCode = 1,
    Unattributed = 2,
}
