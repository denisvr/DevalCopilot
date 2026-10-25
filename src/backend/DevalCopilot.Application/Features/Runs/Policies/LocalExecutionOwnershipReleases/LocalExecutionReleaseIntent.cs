using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>
/// The typed intent snapshot stored in the release row (ADR-0066), read exactly as stored: one member per canonical confirmed fact. The
/// release reading compares every member with the current proved facts, field for field; a missing (null) member, an unsupported version
/// or any difference keeps the operation owning. Enum members are read as their typed values: the stored vocabulary is constrained by the
/// table, and a value outside it makes the whole reading Unavailable instead of a partial answer.
/// </summary>
internal sealed record LocalExecutionReleaseIntent(
    int? Version,
    LocalExecutionOperationFamily? Family,
    Guid? OperationId,
    Guid? SequenceId,
    Guid? RunId,
    Guid? ProjectId,
    Guid? WorkspaceId,
    LocalExecutionSequenceState? SequenceState,
    LocalExecutionAdmissionMode? AdmissionMode,
    Guid? AdmissionId,
    LocalExecutionStepKind? Kind,
    int? Ordinal,
    int? PassNumber,
    int? RecipeOrdinal,
    Guid? PredecessorStepId,
    Guid? SourceCheckpointId,
    string? SourceFingerprintSha256,
    Guid? ReportMessageId,
    Guid? SubjectAttemptId,
    AgentResponseContract? AgentResponseContract,
    Guid? RecipeCommandId,
    string? OperationStatus,
    AgentOutcome? AgentOutcome,
    string? CompletionFingerprintSha256,
    ProcessOutcome? ProcessOutcome,
    int? SemanticExitCode,
    TimeSpan? ProcessDuration,
    int? PhysicalExitCode,
    TimeSpan? ObservedElapsed,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? ObservationRecordedAtUtc,
    int? ConfirmationVersion,
    DateTimeOffset? ConfirmationExpiresAtUtc);
