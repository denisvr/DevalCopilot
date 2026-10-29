namespace DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;

/// <summary>
/// The scalar facts of one run, its registered project and one exactly associated <c>run.abandoned</c> event, read by one bounded
/// relational statement (ADR-0071). Identifiers, times, the reason and the event payload are raw stored bytes within their bound (a
/// <see langword="null"/> value beside a <c>Present</c> flag is a rejected value, never an absent one), <see cref="LifecycleCode"/> is 1
/// to 6 in declaration order (0 unknown) and <see cref="AbandonedAtUtc"/> is read through the abandonment column's own TEXT-only
/// tolerant mapping, so a value of any other storage class or form reads as null. No objective, stage, budget, attempt, message,
/// verification, workspace, lease, commit or artifact fact is part of it.
/// </summary>
internal sealed record RecordedAbandonmentRow(
    byte[]? RunId,
    byte[]? RunProjectId,
    byte[]? ProjectId,
    string? StoredMode,
    int LifecycleCode,
    bool NoActiveParticipant,
    byte[]? LastAdvancedAtUtc,
    bool ReasonPresent,
    byte[]? Reason,
    bool AbandonedAtPresent,
    DateTimeOffset? AbandonedAtUtc,
    bool EventPresent,
    byte[]? EventId,
    long? EventSequence,
    byte[]? EventRunId,
    bool EventHumanAndRunScoped,
    byte[]? EventPayload,
    byte[]? EventOccurredAtUtc);
