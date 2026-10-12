namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryAttempt;

/// <summary><see cref="Attempt"/> is non-null exactly when <see cref="Status"/> is <see cref="CollaborationAttemptStatus.Available"/>.</summary>
public sealed record GetCollaborationHistoryAttemptQueryResult(
    Guid RunId, Guid MessageId, long SourceSequence, CollaborationAttemptStatus Status, CollaborationAttemptHeader? Attempt);
