namespace DevalCopilot.Api.Features.Runs.GetPlanningSequence;

/// <summary>The exact final references, present only when proved from durable identity: an accepted plan names its Proposal and
/// Acceptance, an escalated sequence names its depth-two Proposal and the existing Human escalation. They authorize no implementation.</summary>
public sealed record PlanningSequenceFinalResponse(
    Guid FinalProposalMessageId, Guid? AcceptanceMessageId, Guid? EscalationMessageId, Guid ProvingAttemptId);
