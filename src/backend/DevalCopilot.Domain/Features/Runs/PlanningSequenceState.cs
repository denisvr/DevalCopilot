namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed state vocabulary of one explicitly authorized read-only planning sequence (ADR-0055). The string values are durable
/// schema. A read of a run with no sequence reports <c>None</c> at the API boundary; that is never a persisted state.
/// </summary>
public enum PlanningSequenceState
{
    /// <summary>The Human armed the sequence; no step has been reserved yet. Arming invokes nothing and consumes no budget.</summary>
    Armed = 0,

    /// <summary>One step-preparation intent is reserved and only its original live host worker may try to claim it once.</summary>
    PreparingStep = 1,

    /// <summary>The step's attempt was claimed in the same transaction as its link. A claim or a dispatch marker is not proof that a
    /// provider process is running.</summary>
    AwaitingStepResult = 2,

    /// <summary>"Stop after current step" won after a claim: the claimed read-only step may finish, no successor is allowed.</summary>
    Stopping = 3,

    /// <summary>An Accepted root or first revision completed planning. It grants no implementation or current-source approval.</summary>
    ReadyForImplementation = 4,

    /// <summary>The depth-two revision ended at the existing Human escalation. Never ready to implement.</summary>
    AwaitingHumanDecision = 5,

    /// <summary>The Human stopped the sequence before a claim or after the claimed step drained.</summary>
    Stopped = 6,

    /// <summary>Advancement closed with a fixed reason; an outstanding or unknown attempt stays visible as awaiting reconciliation.</summary>
    NeedsAttention = 7,

    /// <summary>A host restart interrupted an advancing sequence. It never resumes, redispatches or adopts a result.</summary>
    Interrupted = 8,
}
