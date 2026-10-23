namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionAdmissionHistory;

/// <summary>
/// One recorded admission (ADR-0063). <c>AdmissionNumber</c> is the recorded admission order, not an exact global queue position, and
/// <c>Revision</c> a record version, both canonical positive Int64 strings. <c>WaitingReason</c> is the last durably recorded observation
/// with its own time, never present capacity, rank, eligibility or a promised start. <c>StepId</c> is recorded structure, not proof of an
/// operation or its outcome; those need the selected step inspection. The recipe name is untrusted text and is rendered inert. Times are
/// UTC round-trip strings.
/// </summary>
public sealed record LocalExecutionAdmissionHistoryAdmissionResponse(
    Guid AdmissionId,
    string AdmissionNumber,
    int Ordinal,
    string Kind,
    int PassNumber,
    int? RecipeOrdinal,
    string? RecipeName,
    string RecordedState,
    string WaitingReason,
    string WaitingReasonRecordedAtUtc,
    string EnqueuedAtUtc,
    string UpdatedAtUtc,
    string Revision,
    string? ReasonCode,
    Guid? StepId);
