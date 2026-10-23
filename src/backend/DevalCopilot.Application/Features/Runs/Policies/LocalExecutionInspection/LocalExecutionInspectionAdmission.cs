using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>
/// One proved recorded admission of a queued cycle (ADR-0063). <see cref="AdmissionNumber"/> is the recorded admission order, never an
/// exact global queue position. <see cref="WaitingReason"/> is the last durably recorded observation with its own time, never present
/// capacity, rank, eligibility or a promised start. <see cref="StepId"/> is recorded structure, not proof of an operation or its outcome;
/// those need the selected step inspection. No reserved operation identity, preparation token, executable, argument, path or digest is
/// carried.
/// </summary>
public sealed record LocalExecutionInspectionAdmission(
    Guid AdmissionId,
    long AdmissionNumber,
    int Ordinal,
    LocalExecutionStepKind Kind,
    int PassNumber,
    int? RecipeOrdinal,
    string? RecipeName,
    LocalExecutionAdmissionState RecordedState,
    string WaitingReason,
    DateTimeOffset WaitingReasonRecordedAtUtc,
    DateTimeOffset EnqueuedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Revision,
    string? ReasonCode,
    Guid? StepId);
