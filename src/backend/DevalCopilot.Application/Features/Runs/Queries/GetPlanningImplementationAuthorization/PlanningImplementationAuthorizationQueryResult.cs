namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningImplementationAuthorization;

/// <summary>
/// The truthful, bounded state of the human implementation authorization of one planning escalation. It states recorded
/// and derived durable facts only, never that a provider is ready, a budget remains, or an implementation will start.
/// <list type="bullet">
/// <item><c>Absent</c>: the escalation is a valid, current source and no authorization was recorded.</item>
/// <item><c>Available</c>: a coherent authorization is recorded, unconsumed, and bound to the current checkpoint.</item>
/// <item><c>Consumed</c>: a coherent authorization was spent by exactly one implementation claim.</item>
/// <item><c>Stale</c>: the source or the recorded authorization no longer matches the run's current planning lineage,
/// checkpoint, or fingerprint, or a newer independent Planner Proposal replaced the lineage.</item>
/// <item><c>Invalid</c>: the recorded or source evidence is incoherent, forged, ambiguous, or unreadable.</item>
/// </list>
/// </summary>
public sealed record PlanningImplementationAuthorizationQueryResult(
    Guid RunId,
    Guid EscalationMessageId,
    PlanningImplementationAuthorizationState State,
    Guid? FinalProposalMessageId,
    IReadOnlyList<Guid> OrderedDecisionMessageIds,
    Guid? AuthorizationId,
    Guid? HumanInstructionMessageId,
    string? Rationale,
    DateTimeOffset? AuthorizedAtUtc,
    Guid? ConsumedByAttemptId,
    DateTimeOffset? ConsumedAtUtc);
