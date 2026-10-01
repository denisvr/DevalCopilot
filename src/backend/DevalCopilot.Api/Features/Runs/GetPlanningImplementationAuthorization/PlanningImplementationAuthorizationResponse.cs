namespace DevalCopilot.Api.Features.Runs.GetPlanningImplementationAuthorization;

/// <summary>
/// The state of the human implementation authorization of one planning escalation: exactly one of <c>Absent</c>,
/// <c>Available</c>, <c>Consumed</c>, <c>Stale</c>, or <c>Invalid</c>. It states recorded and derived durable facts only
/// and never that a provider is ready, a budget remains, or an implementation will start. <c>Rationale</c> is the exact
/// recorded human reason (at most 600 characters), present only when the recorded evidence validated.
/// </summary>
public sealed record PlanningImplementationAuthorizationResponse(
    Guid RunId,
    Guid EscalationMessageId,
    string State,
    Guid? FinalProposalMessageId,
    IReadOnlyList<Guid> OrderedDecisionMessageIds,
    Guid? AuthorizationId,
    Guid? HumanInstructionMessageId,
    string? Rationale,
    DateTimeOffset? AuthorizedAtUtc,
    Guid? ConsumedByAttemptId,
    DateTimeOffset? ConsumedAtUtc);
