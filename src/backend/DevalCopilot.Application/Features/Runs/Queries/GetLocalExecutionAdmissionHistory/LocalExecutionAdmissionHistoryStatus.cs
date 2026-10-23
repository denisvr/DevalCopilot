namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionAdmissionHistory;

/// <summary><see cref="NotRecorded"/>: the known run has no sequence. <see cref="NotQueued"/>: a coherent Immediate sequence has no
/// admission rows. <see cref="Recorded"/>: a coherent queued sequence with its complete nonempty ledger. <see cref="Unavailable"/>: a
/// present root, structure or linkage that cannot be proved, and nothing of it is shown.</summary>
public enum LocalExecutionAdmissionHistoryStatus
{
    NotRecorded,
    NotQueued,
    Recorded,
    Unavailable,
}
