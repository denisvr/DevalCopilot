namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationMessageEvidence;

/// <summary>
/// The closed, honest shape of one attempt's recorded, ordered <c>AttemptInputMessage</c> set —
/// never a complete prompt, complete context manifest, or resumable provider session, only the
/// durable references an Agent attempt was launched against. Always read this discriminator first;
/// never inferred from <see cref="CollaborationMessageEvidenceQueryResult.InputMessages"/>'s own
/// emptiness alone, since both <see cref="Empty"/> and <see cref="Invalid"/> also present an empty
/// list.
/// </summary>
public enum AttemptInputMessageEvidenceStatus
{
    /// <summary>This attempt genuinely recorded no input messages. Reachable ONLY for a
    /// <see cref="DevalCopilot.Domain.Features.Runs.AgentRole.Planner"/> attempt, which starts from
    /// no prior collaboration fact — every other current role always persists at least one
    /// required row, so zero rows for any of them is <see cref="Invalid"/>, never this. Not an
    /// error, and not the same as an unverifiable set.</summary>
    Empty,

    /// <summary>The attempt's persisted input set is coherent: its stored sequence is gapless and
    /// duplicate-free starting at 0, and every referenced message resolves to a real
    /// <c>CollaborationMessage</c> row belonging to the same run. <see cref="CollaborationMessageEvidenceQueryResult.InputMessages"/>
    /// presents it in that stored order, bounded by a small explicit cap (see
    /// <see cref="CollaborationMessageEvidenceQueryResult.InputMessagesOmitted"/>).</summary>
    Recorded,

    /// <summary>The persisted input set could not be trusted — a gap or duplicate in the stored
    /// sequence, an entry whose referenced message is missing or belongs to a different run, or zero
    /// rows for any role other than <see cref="DevalCopilot.Domain.Features.Runs.AgentRole.Planner"/>
    /// (every other role always persists at least one required row, so an observed-empty set for one
    /// of them is indistinguishable from a lost or corrupted set). The gap/duplicate/foreign-reference
    /// shapes should be impossible under <see cref="DevalCopilot.Domain.Features.Runs.AttemptInputMessage"/>'s
    /// own construction and unique-index invariants; this status exists so the read side fails
    /// closed — never partially presenting the resolvable subset, and never confusing a lost input
    /// set with a legitimate empty one — if any of them is ever observed.</summary>
    Invalid,
}
