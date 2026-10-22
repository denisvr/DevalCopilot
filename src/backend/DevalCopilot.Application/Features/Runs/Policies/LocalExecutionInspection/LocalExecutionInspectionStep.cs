using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;

/// <summary>One proved recorded step. <see cref="OperationId"/> is the recorded link, not proof of the operation or of any outcome; those
/// need the selected inspection.</summary>
public sealed record LocalExecutionInspectionStep(
    Guid StepId,
    int Ordinal,
    LocalExecutionStepKind Kind,
    int PassNumber,
    int? RecipeOrdinal,
    string? RecipeName,
    LocalExecutionSequenceStepState RecordedState,
    string? ReasonCode,
    Guid SourceCheckpointId,
    string SourceFingerprintSha256,
    Guid? ReportMessageId,
    Guid? SubjectAttemptId,
    Guid? OperationId,
    DateTimeOffset PreparationReservedAtUtc,
    DateTimeOffset PreparationDeadlineUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? ResultDeadlineUtc);
