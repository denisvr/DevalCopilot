namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed state vocabulary of one explicitly authorized local execution sequence (ADR-0060). The string values are durable schema.
/// A read of a run with no sequence reports <c>None</c> at the API boundary; that is never a persisted state.
/// </summary>
public enum LocalExecutionSequenceState
{
    /// <summary>The Human armed the sequence; nothing was reserved. Arming invokes and claims nothing and consumes no budget.</summary>
    Armed = 0,

    /// <summary>One step-preparation intent is reserved and only its original live host worker may try to claim it once.</summary>
    PreparingStep = 1,

    /// <summary>The step's operation was claimed in the same transaction as its link. A claim or a dispatch marker is not proof that
    /// a process is running.</summary>
    AwaitingStepResult = 2,

    /// <summary>"Stop after current operation" won after a claim: that one operation may finish, no successor is allowed.</summary>
    Stopping = 3,

    /// <summary>An ordinary review approved the exact linked report. It awaits the existing Human approval and commit decisions.</summary>
    ReadyForHumanApproval = 4,

    /// <summary>The cycle ended at a Human escalation (the correction allowance was reached or the diagnosis escalated).</summary>
    HumanEscalation = 5,

    /// <summary>The Human stopped the sequence before a claim or after the claimed operation drained.</summary>
    Stopped = 6,

    /// <summary>Advancement closed with a fixed reason; an outstanding or unknown operation stays visible as awaiting reconciliation.</summary>
    NeedsAttention = 7,

    /// <summary>A host restart closed advancement. It never resumes, redispatches or adopts a result.</summary>
    Interrupted = 8,
}
