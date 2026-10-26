namespace DevalCopilot.Application.Features.Runs.Policies.PlanningSequenceInspection;

/// <summary>The scalar identity of one input message: its run and attributing attempt as raw stored bytes, and whether it is a Proposal.
/// Its content, summary and structured payload are never selected.</summary>
internal sealed record PlanningSequenceInspectionMessageRow(
    byte[]? Id, byte[]? RunId, byte[]? AttemptId, bool AttemptPresent, bool IsProposal);
