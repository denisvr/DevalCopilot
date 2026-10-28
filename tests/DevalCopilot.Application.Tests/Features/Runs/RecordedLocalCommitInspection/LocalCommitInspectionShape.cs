namespace DevalCopilot.Application.Tests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>The recorded status/time/reason tuples a real operation can hold (ADR-0070), including Failed, Interrupted and
/// NeedsAttention with and without the single-use execution marker.</summary>
public enum LocalCommitInspectionShape
{
    Prepared,
    Executing,
    Completed,
    FailedStarted,
    FailedUnstarted,
    InterruptedStarted,
    InterruptedUnstarted,
    AttentionStarted,
    AttentionUnstarted,
}
