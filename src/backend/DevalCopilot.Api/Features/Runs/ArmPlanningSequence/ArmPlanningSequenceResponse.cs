namespace DevalCopilot.Api.Features.Runs.ArmPlanningSequence;

/// <summary>The recorded arming receipt: the immutable pinned tuple and the current state. <c>Replayed</c> is true when the identical
/// tuple was already armed; a replay never rearms. It proves no runtime readiness: arming invoked no provider, prepared no workspace and
/// consumed no budget. <c>Revision</c> is a canonical Int64 string and <c>ArmedAtUtc</c> a UTC round-trip string.</summary>
public sealed record ArmPlanningSequenceResponse(
    Guid SequenceId,
    Guid RunId,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    string State,
    string ArmedAtUtc,
    string Revision,
    bool Replayed);
