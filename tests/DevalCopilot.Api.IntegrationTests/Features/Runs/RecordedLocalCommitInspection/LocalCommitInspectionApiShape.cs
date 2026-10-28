namespace DevalCopilot.Api.IntegrationTests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>The recorded status/time tuples a real operation can hold (ADR-0070), including Failed, Interrupted and NeedsAttention with
/// and without the single-use execution marker.</summary>
public enum LocalCommitInspectionApiShape
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
