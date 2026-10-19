namespace DevalCopilot.Api.Features.Runs.GetPlanningSequence;

/// <summary>The recorded arming availability of a run without a sequence: the exact candidate tuple, or one fixed refusal code with its
/// fixed text. It performs no Git observation and proves no runtime readiness; arming always checks afresh.</summary>
public sealed record PlanningSequenceArmingResponse(
    bool Available, string? RefusalCode, string? RefusalText, Guid? WorkspaceId, Guid? CheckpointId, string? FingerprintSha256);
