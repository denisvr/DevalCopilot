using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// Constant-memory per-provider partition of <see cref="RunCockpitTokenUsageAccumulator"/>: one
/// independent accumulator per <see cref="RunCockpitProviderTokenUsageAttribution"/> bucket, fed from
/// the same single pass over dispatched Agent attempts the run-wide accumulator already consumes. A
/// still-<see cref="AttemptStatus.Running"/> attempt's usage is never trusted, regardless of what its
/// persisted row already shows — each bucket's own accumulator enforces that independently, exactly as
/// the run-wide one does.
/// </summary>
internal sealed class RunCockpitProviderTokenUsageAccumulator
{
    private static readonly RunCockpitProviderTokenUsageAttribution[] AttributionOrder =
    [
        RunCockpitProviderTokenUsageAttribution.Codex,
        RunCockpitProviderTokenUsageAttribution.ClaudeCode,
        RunCockpitProviderTokenUsageAttribution.Unattributed,
    ];

    private readonly Dictionary<RunCockpitProviderTokenUsageAttribution, RunCockpitTokenUsageAccumulator> _accumulators =
        AttributionOrder.ToDictionary(attribution => attribution, _ => new RunCockpitTokenUsageAccumulator());

    public void Add(AttemptStatus status, AgentProvider? provider, AgentTokenUsageEvidence? usage) =>
        _accumulators[AttributionFor(provider)].Add(status, usage);

    public IReadOnlyList<RunCockpitProviderTokenUsageEntry> ToEntries() =>
        AttributionOrder
            .Select(attribution => new RunCockpitProviderTokenUsageEntry(attribution, _accumulators[attribution].ToSummary()))
            .ToArray();

    /// <summary>Buckets a dispatched Agent attempt by its recorded provider. A null provider, or any
    /// provider value that is not one of the two providers this projection currently distinguishes,
    /// falls closed to <see cref="RunCockpitProviderTokenUsageAttribution.Unattributed"/> rather than
    /// being dropped or guessed at.</summary>
    internal static RunCockpitProviderTokenUsageAttribution AttributionFor(AgentProvider? provider) => provider switch
    {
        AgentProvider.Codex => RunCockpitProviderTokenUsageAttribution.Codex,
        AgentProvider.ClaudeCode => RunCockpitProviderTokenUsageAttribution.ClaudeCode,
        _ => RunCockpitProviderTokenUsageAttribution.Unattributed,
    };
}
