namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionAdmissionHistory;

/// <summary>
/// The stored bookkeeping of the queued sequence (ADR-0063): never effective live control, a final approval or commit proof, a
/// preparation owner token, a settings snapshot or a digest. <c>SequenceRevision</c> is a canonical Int64 string and times are UTC
/// round-trip strings. <c>AdvancementDeadlineUtc</c> is the recorded absolute two-hour deadline from arming.
/// </summary>
public sealed record LocalExecutionAdmissionHistoryHeaderResponse(
    Guid SequenceId,
    string AdmissionMode,
    string RecordedState,
    string? ReasonCode,
    string SequenceRevision,
    string ArmedAtUtc,
    string AdvancementDeadlineUtc,
    string UpdatedAtUtc,
    string? EndedAtUtc);
