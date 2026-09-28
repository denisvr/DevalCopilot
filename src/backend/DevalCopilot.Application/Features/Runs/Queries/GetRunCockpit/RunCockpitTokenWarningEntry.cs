using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// One provider's advisory token-activity warning projection for a run. Counts cover only
/// dispatched Agent attempts (an undispatched attempt never invoked a provider).
/// </summary>
/// <param name="Provider">Codex or Claude Code; the two are never combined.</param>
/// <param name="ThresholdTokens">The owner's configured threshold, or null when none is set.</param>
/// <param name="State">The advisory state; see <see cref="RunCockpitTokenWarningState"/>.</param>
/// <param name="KnownTokenCount">The sum over <paramref name="CountedAttempts"/> of the provider's
/// own formula (Codex: input + output; Claude Code: input + cache creation + cache read + output),
/// each component exactly once. A lower bound whenever any gap count below is non-zero.</param>
/// <param name="CountedAttempts">Concluded attempts of this provider with sufficient evidence.</param>
/// <param name="PendingAttempts">Still-running attempts of this provider; never counted.</param>
/// <param name="InsufficientEvidenceAttempts">Concluded attempts of this provider whose usage is
/// missing, malformed, unsupported, or (Claude Code) lacks a cache-creation or cache-read count.</param>
/// <param name="UnattributedAttempts">Dispatched attempts with no known provider, run-wide. They are
/// never assigned to either provider and are a gap for both.</param>
public sealed record RunCockpitTokenWarningEntry(
    AgentProvider Provider,
    long? ThresholdTokens,
    RunCockpitTokenWarningState State,
    long KnownTokenCount,
    int CountedAttempts,
    int PendingAttempts,
    int InsufficientEvidenceAttempts,
    int UnattributedAttempts);
