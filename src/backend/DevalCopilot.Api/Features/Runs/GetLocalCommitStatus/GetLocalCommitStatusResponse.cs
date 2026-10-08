using DevalCopilot.Api.Features.Runs.RequestLocalCommit;

namespace DevalCopilot.Api.Features.Runs.GetLocalCommitStatus;

/// <summary>Advisory eligibility plus the recorded operation. <c>Eligible</c> never authorizes anything: the command decides again.</summary>
public sealed record GetLocalCommitStatusResponse(
    bool Eligible,
    string? RefusalCode,
    Guid? CheckpointId,
    int? CheckpointNumber,
    Guid? CodeReviewAttemptId,
    Guid? HumanCheckpointReviewId,
    LocalCommitOperationResponse? Operation,
    long LatestEventSequence);
