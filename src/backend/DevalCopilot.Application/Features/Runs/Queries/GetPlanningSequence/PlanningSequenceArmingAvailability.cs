namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningSequence;

/// <summary>The recorded admission facts for a run without a sequence: the exact candidate tuple, or one fixed refusal code. It
/// performs no Git observation and POST always checks afresh.</summary>
public sealed record PlanningSequenceArmingAvailability(
    bool Available, string? RefusalCode, Guid? WorkspaceId, Guid? CheckpointId, string? FingerprintSha256);
