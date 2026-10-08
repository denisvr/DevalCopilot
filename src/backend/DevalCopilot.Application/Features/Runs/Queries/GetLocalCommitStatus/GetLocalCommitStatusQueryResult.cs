namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitStatus;

/// <param name="RefusalCode">A fixed code explaining why the run cannot request a commit now; null when eligible.</param>
/// <param name="CheckpointId">The exact current candidate the form would submit; null when none can be identified.</param>
public sealed record GetLocalCommitStatusQueryResult(
    bool Eligible,
    string? RefusalCode,
    Guid? CheckpointId,
    int? CheckpointNumber,
    Guid? CodeReviewAttemptId,
    Guid? HumanCheckpointReviewId,
    LocalCommitOperationView? Operation,
    long LatestEventSequence);
