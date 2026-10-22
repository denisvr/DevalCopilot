namespace DevalCopilot.Api.Features.Runs.QueueLocalExecutionSequence;

/// <summary>The only caller-supplied facts of an explicit queued local-execution authorization (ADR-0062): exactly the five string
/// members of the Immediate arming (ADR-0060), the exact plan, workspace, checkpoint and 64-lowercase-hexadecimal fingerprint the Human
/// saw and the 64-lowercase-hexadecimal queue confirmation of the queue availability read. The route selects the queued mode; there is no
/// scheduling priority, deadline, budget, reservation, effect, host token or operation identifier. Unknown, duplicate, differently cased,
/// escaped and mistyped members are refused before anything is dispatched.</summary>
public sealed record QueueLocalExecutionSequenceRequest
{
    public required Guid PlanProposalMessageId { get; init; }

    public required Guid WorkspaceId { get; init; }

    public required Guid CheckpointId { get; init; }

    public required string FingerprintSha256 { get; init; }

    public required string ConfirmationSha256 { get; init; }
}
