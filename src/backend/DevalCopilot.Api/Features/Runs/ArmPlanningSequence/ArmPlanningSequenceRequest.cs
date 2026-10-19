namespace DevalCopilot.Api.Features.Runs.ArmPlanningSequence;

/// <summary>The only caller-supplied facts of an explicit planning-sequence arming (ADR-0055): the exact workspace, checkpoint and
/// 64-lowercase-hexadecimal fingerprint the Human saw. There is no role, provider, permission, step, mode or process input: the host
/// decides everything else from fresh authority. Unknown, duplicate, differently cased, escaped and mistyped members are refused before
/// anything is dispatched.</summary>
public sealed record ArmPlanningSequenceRequest
{
    public required Guid WorkspaceId { get; init; }

    public required Guid CheckpointId { get; init; }

    public required string FingerprintSha256 { get; init; }
}
