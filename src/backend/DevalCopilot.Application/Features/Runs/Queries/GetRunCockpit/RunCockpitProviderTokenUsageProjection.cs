using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// Splits the same dispatched-Agent-attempt token-usage evidence the run-wide
/// <see cref="RunCockpitTokenUsageSummary"/> sums into one independent summary per provider bucket,
/// so a reader can see Codex, Claude Code, and any unattributed dispatched attempts separately
/// without changing or replacing the existing run-wide total, which is computed separately and is
/// unaffected by this projection. Always returns exactly three entries, one per
/// <see cref="RunCockpitProviderTokenUsageAttribution"/> value in a fixed order, even when a bucket
/// has no dispatched attempts at all — a stable shape a reader or UI can always index by attribution
/// rather than searching a variable-length list. This uses only already-recorded, already-trusted
/// per-attempt evidence (the same <see cref="AgentProvider"/> and <see cref="AgentTokenUsageEvidence"/>
/// every other cockpit token-usage read path already trusts); it observes no new provider surface and
/// adds no new adapter.
/// </summary>
public static class RunCockpitProviderTokenUsageProjection
{
    /// <summary>Single-pass, constant-memory aggregation over dispatched Agent attempts, mirroring
    /// <see cref="RunCockpitTokenUsageSummary.FromDispatchedAttempts"/> but partitioned by provider
    /// first.</summary>
    public static IReadOnlyList<RunCockpitProviderTokenUsageEntry> FromDispatchedAttempts(
        IEnumerable<(AttemptStatus Status, AgentProvider? Provider, AgentTokenUsageEvidence? Usage)> dispatchedAttempts)
    {
        ArgumentNullException.ThrowIfNull(dispatchedAttempts);

        var accumulator = new RunCockpitProviderTokenUsageAccumulator();
        foreach (var (status, provider, usage) in dispatchedAttempts)
        {
            accumulator.Add(status, provider, usage);
        }

        return accumulator.ToEntries();
    }
}
