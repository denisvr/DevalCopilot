namespace DevalCopilot.Domain.Features.Runs;

/// <summary>The durable state of one step of a planning sequence (ADR-0055). At most one step per sequence is outstanding
/// (<see cref="Reserved"/> or <see cref="Claimed"/>).</summary>
public enum PlanningSequenceStepState
{
    /// <summary>The preparation intent is reserved; no claim has committed. Its authority belongs to one live host worker.</summary>
    Reserved = 0,

    /// <summary>The step, its attempt, inputs, artifact metadata and budget consumption committed in one transaction.</summary>
    Claimed = 1,

    /// <summary>The claimed attempt reached a terminal result that the coordinator classified.</summary>
    Concluded = 2,

    /// <summary>The preparation authority was revoked before any claim committed: no attempt and no budget were consumed.</summary>
    Revoked = 3,

    /// <summary>A failed preparation could not be proved either persisted or absent. Nothing is retried.</summary>
    Unresolved = 4,
}
