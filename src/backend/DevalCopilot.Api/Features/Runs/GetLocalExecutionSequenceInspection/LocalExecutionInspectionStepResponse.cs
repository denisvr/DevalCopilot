namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;

/// <summary>
/// One recorded step: its fixed kind, pass and recipe position, its recorded state and fixed reason, its exact source checkpoint and
/// fingerprint, the report and subject it names and its recorded link. <c>OperationId</c> is a recorded link, not proof of the operation or
/// of any outcome; those need the selected inspection. The recipe name is untrusted text and is rendered inert. Times are UTC round-trip
/// strings.
/// </summary>
public sealed record LocalExecutionInspectionStepResponse(
    Guid StepId,
    int Ordinal,
    string Kind,
    int PassNumber,
    int? RecipeOrdinal,
    string? RecipeName,
    string RecordedState,
    string? ReasonCode,
    Guid SourceCheckpointId,
    string SourceFingerprintSha256,
    Guid? ReportMessageId,
    Guid? SubjectAttemptId,
    Guid? OperationId,
    string PreparationReservedAtUtc,
    string PreparationDeadlineUtc,
    string? ClaimedAtUtc,
    string? ResultDeadlineUtc);
