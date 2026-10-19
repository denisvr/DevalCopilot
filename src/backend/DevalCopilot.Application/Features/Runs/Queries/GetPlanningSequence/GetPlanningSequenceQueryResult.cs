using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;

/// <param name="State">The effective state (an elapsed deadline reads as NeedsAttention before the sweep persists it; the snapshot keeps the recorded state), or <see langword="null"/> when the run has no sequence (reported as None, never persisted).</param>
/// <param name="LatestEventSequence">The newest recorded run event, for revalidation after a notification; 0 when none exists.</param>
public sealed record GetPlanningSequenceQueryResult(
    Guid RunId,
    PlanningSequenceState? State,
    PlanningSequenceSnapshot? Sequence,
    PlanningSequenceArmingAvailability? Arming,
    PlanningSequenceManualFence ManualFence,
    long LatestEventSequence);
