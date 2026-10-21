namespace DevalCopilot.Api.Features.Runs.ArmLocalExecutionSequence;

/// <summary>The recorded arming receipt: the immutable pinned tuple and the current state. <c>Replayed</c> is true when the identical
/// tuple was already armed; a replay never rearms. It proves no runtime readiness: arming invoked no provider, ran no verification,
/// claimed no attempt and consumed no budget. <c>Revision</c> is a canonical Int64 string and every time a UTC round-trip string.</summary>
public sealed record ArmLocalExecutionSequenceResponse(
    Guid SequenceId,
    Guid RunId,
    Guid PlanProposalMessageId,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    string ConfirmationSha256,
    string State,
    int RecipeCount,
    int CorrectionAllowance,
    string ArmedAtUtc,
    string AdvancementDeadlineUtc,
    string Revision,
    bool Replayed);
