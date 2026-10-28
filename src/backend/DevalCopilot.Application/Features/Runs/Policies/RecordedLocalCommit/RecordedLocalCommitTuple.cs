using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;

/// <summary>
/// The complete supported status, time, reason and result-identity tuple of a recorded local-commit operation (ADR-0070), as the Domain
/// records it. Prepared has no start, completion or reason; Executing has a start at or after creation and nothing else; Completed has a
/// start at or after creation, a completion at or after the start, no reason and a valid recorded commit identity; Failed and Interrupted
/// have a completion at or after creation, a reason and an optional start (the single-use marker may never have been written) between
/// creation and completion; NeedsAttention has a reason, no completion and an optional start at or after creation. No dispatch or
/// completion time is ever invented for a state that lacks one, and a commit identity exists only for Completed.
/// </summary>
internal static class RecordedLocalCommitTuple
{
    public static bool TryClassify(
        RecordedLocalCommitRow row,
        DateTimeOffset created,
        DateTimeOffset? started,
        DateTimeOffset? completed,
        out LocalCommitStatus status,
        out RecordedLocalCommitReason reason)
    {
        status = default;
        reason = RecordedLocalCommitReason.None;
        if (row.StatusCode is < 1 or > 6)
        {
            return false;
        }

        status = (LocalCommitStatus)(row.StatusCode - 1);
        if (row.ChangedPathCount is null || row.TotalBytes is null || !IsCommitIdentityCoherent(row, status) || !IsReasonCoherent(row, status))
        {
            return false;
        }

        if (row.ReasonPresent)
        {
            reason = RecordedLocalCommitReasons.Describe(row.Reason!);
        }

        return status switch
        {
            LocalCommitStatus.Prepared => started is null && completed is null,
            LocalCommitStatus.Executing => started >= created && completed is null,
            LocalCommitStatus.Completed => started >= created && completed >= started,
            LocalCommitStatus.Failed or LocalCommitStatus.Interrupted =>
                completed >= created && (started is null || (started >= created && started <= completed)),
            _ => completed is null && (started is null || started >= created),
        };
    }

    private static bool IsCommitIdentityCoherent(RecordedLocalCommitRow row, LocalCommitStatus status) =>
        status != LocalCommitStatus.Completed
            ? row.CommitSha is null
            : !row.CommitShaOversized && RecordedLocalCommitStorage.IsObjectId(row.CommitSha);

    private static bool IsReasonCoherent(RecordedLocalCommitRow row, LocalCommitStatus status)
    {
        var requiresReason = status is LocalCommitStatus.Failed or LocalCommitStatus.Interrupted or LocalCommitStatus.NeedsAttention;
        if (!requiresReason)
        {
            return !row.ReasonPresent;
        }

        return row.ReasonPresent
            && !row.ReasonOversized
            && !string.IsNullOrWhiteSpace(row.Reason)
            && row.Reason.Length <= RecordedLocalCommitStorage.MaximumReasonLength;
    }
}
