namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// Evidence-state classification for <see cref="RunCockpitAgentProcessDurationSummary"/> — a
/// run-wide, read-only summary of HOST-MEASURED Agent process-duration evidence. Deliberately
/// distinct from <see cref="RunTokenUsageCompleteness"/> (a different evidence dimension covering
/// provider-reported tokens, not host-measured duration) and from either Agent budget's own state
/// (<see cref="GetRunCockpitQueryResult.AgentBudgetExhausted"/> and
/// <see cref="RunCockpitAgentInvocationTimeBudgetSummary"/>): this classification never enforces a
/// limit and never asserts a budget, only what evidence has actually been recorded so far.
/// </summary>
public enum AgentProcessDurationEvidenceStatus
{
    /// <summary>This run has dispatched no Agent attempt at all.</summary>
    NoDispatchedAttempts = 0,

    /// <summary>Every dispatched Agent attempt has reached a terminal result, and every one of
    /// them carries valid host-measured process-duration evidence.</summary>
    Complete = 1,

    /// <summary>At least one dispatched Agent attempt is still running (no terminal result yet),
    /// and every terminal attempt observed so far carries valid evidence.</summary>
    PendingEvidence = 2,

    /// <summary>At least one terminal attempt carries valid evidence and at least one other
    /// terminal attempt has missing or malformed evidence — with or without attempts still
    /// pending.</summary>
    PartialEvidence = 3,

    /// <summary>One or more terminal attempts exist, but none of them carries valid evidence — with
    /// or without attempts still pending. Never conflated with a real zero-duration measurement,
    /// which is <see cref="Complete"/> with a zero
    /// <see cref="RunCockpitAgentProcessDurationSummary.TotalMeasuredDuration"/> instead, and never
    /// described as if every dispatched attempt had reached a terminal result when
    /// <see cref="RunCockpitAgentProcessDurationSummary.PendingAttemptCount"/> is nonzero.</summary>
    MalformedEvidence = 4,

    /// <summary>Every terminal attempt observed so far carries valid host-measured evidence (no
    /// malformed evidence at all), but the exact running sum of those valid durations overflows what
    /// a <see cref="TimeSpan"/> can represent — with or without attempts still pending. The
    /// individual measurements are real and valid; only their total is unrepresentable, which is why
    /// this is a distinct state from <see cref="MalformedEvidence"/> rather than folded into it.
    /// <see cref="RunCockpitAgentProcessDurationSummary.TotalMeasuredDuration"/> stays
    /// <see langword="null"/>, matching every other non-<see cref="Complete"/> state.</summary>
    UnrepresentableTotal = 5,
}
