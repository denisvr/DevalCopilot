namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// ADR-0065 and ADR-0066: the complete, closed tuple of historical facts one exact release confirmation names, read afresh from the
/// database by the Application, bound by their canonical digest (<see cref="LocalExecutionOwnershipReleaseConfirmation"/>) and stored
/// field for field as typed columns of the receipt (<see cref="LocalExecutionOwnershipRelease"/>), so every later reader compares actual
/// values. It includes the concrete Agent outcome or verification completion fingerprint, never only their coherence class. It carries no
/// host launch identity, nonce, process identifier, path, argument, environment value or provider output.
/// </summary>
public sealed record LocalExecutionOwnershipReleaseFacts(
    Guid WitnessId,
    Guid ObservationId,
    LocalExecutionOperationFamily Family,
    Guid OperationId,
    Guid SequenceId,
    Guid RunId,
    Guid ProjectId,
    Guid WorkspaceId,
    LocalExecutionSequenceState SequenceState,
    LocalExecutionAdmissionMode AdmissionMode,
    Guid? AdmissionId,
    Guid StepId,
    LocalExecutionStepKind Kind,
    int Ordinal,
    int PassNumber,
    int? RecipeOrdinal,
    Guid? PredecessorStepId,
    Guid SourceCheckpointId,
    string SourceFingerprintSha256,
    Guid? ReportMessageId,
    Guid? SubjectAttemptId,
    AgentResponseContract? AgentResponseContract,
    Guid? RecipeCommandId,
    string OperationStatus,
    AgentOutcome? AgentOutcome,
    string? CompletionFingerprintSha256,
    ProcessOutcome ProcessOutcome,
    int? SemanticExitCode,
    long ProcessDurationTicks,
    int PhysicalExitCode,
    long ObservedElapsedTicks,
    DateTimeOffset DispatchedAtUtc,
    DateTimeOffset RecordedAtUtc);
