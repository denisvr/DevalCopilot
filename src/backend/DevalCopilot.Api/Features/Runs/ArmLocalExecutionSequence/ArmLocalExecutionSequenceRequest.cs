namespace DevalCopilot.Api.Features.Runs.ArmLocalExecutionSequence;

/// <summary>The only caller-supplied facts of an explicit local-execution-sequence arming (ADR-0060): the exact plan, workspace,
/// checkpoint and 64-lowercase-hexadecimal fingerprint the Human saw, and the 64-lowercase-hexadecimal confirmation value of the
/// availability read. There is no command text, provider choice, permission, budget, child identifier or guidance: the host decides
/// everything else from fresh authority. Unknown, duplicate, differently cased, escaped and mistyped members are refused before anything
/// is dispatched.</summary>
public sealed record ArmLocalExecutionSequenceRequest
{
    public required Guid PlanProposalMessageId { get; init; }

    public required Guid WorkspaceId { get; init; }

    public required Guid CheckpointId { get; init; }

    public required string FingerprintSha256 { get; init; }

    public required string ConfirmationSha256 { get; init; }
}
