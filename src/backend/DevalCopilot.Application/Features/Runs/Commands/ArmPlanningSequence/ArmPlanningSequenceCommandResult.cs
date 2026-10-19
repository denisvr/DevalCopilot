using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.ArmPlanningSequence;

/// <summary>The recorded receipt of an arming: the immutable tuple, the current state and whether this answer replays an earlier
/// arming. It proves no runtime readiness and no provider or process activity.</summary>
public sealed record ArmPlanningSequenceCommandResult(
    Guid SequenceId,
    Guid RunId,
    Guid WorkspaceId,
    Guid CheckpointId,
    string FingerprintSha256,
    PlanningSequenceState State,
    DateTimeOffset ArmedAtUtc,
    long Revision,
    bool Replayed);
