namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;

/// <summary>The manual-control fence of a sequence as read now. <c>Released</c> lifts only this fence and never asserts that an
/// ordinary manual operation is eligible.</summary>
public enum PlanningSequenceManualFence
{
    Blocked,
    Released,
    Unavailable,
}
