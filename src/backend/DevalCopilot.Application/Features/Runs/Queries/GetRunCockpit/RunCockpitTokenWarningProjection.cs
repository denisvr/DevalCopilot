using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// Derives the per-provider advisory token-activity warnings from the same dispatched-attempt
/// evidence (already reconstructed through <see cref="AgentTokenUsageEvidence.FromPersisted"/>, which
/// enforces the proven provider/schema pair) that the cockpit's other token projections consume.
/// Always returns exactly two entries, Codex then Claude Code. Changing a threshold only changes the
/// inputs to this pure derivation, so existing evidence is re-evaluated with no provider invocation.
/// </summary>
public static class RunCockpitTokenWarningProjection
{
    public static IReadOnlyList<RunCockpitTokenWarningEntry> FromDispatchedAttempts(
        long? codexThreshold,
        long? claudeThreshold,
        IEnumerable<(AttemptStatus Status, AgentProvider? Provider, AgentTokenUsageEvidence? Usage)> dispatchedAttempts)
    {
        ArgumentNullException.ThrowIfNull(dispatchedAttempts);

        var accumulator = new RunCockpitTokenWarningAccumulator();
        foreach (var (status, provider, usage) in dispatchedAttempts)
        {
            accumulator.Add(status, provider, usage);
        }

        return accumulator.ToEntries(codexThreshold, claudeThreshold);
    }
}
