namespace DevalCopilot.Api.Features.Runs.RequestLocalCommit;

/// <summary>The only caller-supplied facts of an explicit local commit; the host derives every path, ref, SHA and Git argument.
/// <c>Message</c> is the human-written commit message: trimmed, LF line endings, a nonempty subject, no control characters and at
/// most 2 KiB of UTF-8.</summary>
public sealed record RequestLocalCommitRequest
{
    public required Guid OperationId { get; init; }

    public required Guid CheckpointId { get; init; }

    public required Guid CodeReviewAttemptId { get; init; }

    public required Guid HumanCheckpointReviewId { get; init; }

    public required string Message { get; init; }
}
