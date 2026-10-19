namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;

/// <summary>The exact final references proved from durable identity (never a latest message or a loaded window). Ready planning names
/// the accepted Proposal, its Acceptance and the accepting review attempt; an escalated sequence names the depth-two Proposal, the
/// existing Human escalation and the attempt that produced it.</summary>
public sealed record PlanningSequenceFinalReferences(
    Guid FinalProposalMessageId, Guid? AcceptanceMessageId, Guid? EscalationMessageId, Guid ProvingAttemptId);
