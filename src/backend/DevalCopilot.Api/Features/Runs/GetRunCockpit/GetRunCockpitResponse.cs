namespace DevalCopilot.Api.Features.Runs.GetRunCockpit;

public sealed record GetRunCockpitResponse(
    Guid RunId,
    Guid ProjectId,
    string ProjectName,
    int ExecutionNumber,
    string Objective,
    string Lifecycle,
    string Stage,
    string ActiveParticipant,
    double AutonomousDurationSeconds,
    long LatestSequence,
    IReadOnlyList<StageMapEntryResponse> StageMap,
    bool CanPause,
    bool CanStop);
