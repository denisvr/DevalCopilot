namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// The bounded identifiers and exact rationale of the human authorization that one implementation attempt consumed,
/// carried only by internal expectations (the eligibility feed, the dispatch gate, and the invocation request) so that
/// the sealed manifest can be checked against the attempt's durable facts. It is never a prompt, never a provider
/// setting, and never an authority by itself: every consumer proves it afresh from durable identity.
/// </summary>
public sealed record PlanningImplementationAuthorizationFact(
    Guid AuthorizationId,
    Guid EscalationMessageId,
    Guid FinalProposalMessageId,
    Guid HumanInstructionMessageId,
    string Rationale);
