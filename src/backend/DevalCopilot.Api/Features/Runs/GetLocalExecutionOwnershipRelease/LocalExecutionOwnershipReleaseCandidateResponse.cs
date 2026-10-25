namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionOwnershipRelease;

/// <summary>
/// The one release candidate (ADR-0065): the proved sequence, source and operation, the terminal result with its process outcome, the
/// distinct physical termination facts of the original host's observation (an exact Int32 exit code and nonnegative Int64 tick strings;
/// zero is valid) and the exact versioned confirmation that is valid until <c>ConfirmationExpiresAtUtc</c> (at most two minutes after the
/// read). Physical termination is evidence about the invoked process only, never every descendant, and it does not change the failed
/// result. The confirmation is an exact-intent check, not a credential or process proof.
/// </summary>
public sealed record LocalExecutionOwnershipReleaseCandidateResponse(
    Guid SequenceId,
    string SequenceState,
    string AdmissionMode,
    Guid SourceCheckpointId,
    string SourceFingerprintSha256,
    string StepKind,
    int Ordinal,
    int PassNumber,
    string OperationFamily,
    Guid OperationId,
    string OperationStatus,
    string ProcessOutcome,
    int? SemanticExitCode,
    Guid ObservationId,
    int PhysicalExitCode,
    string ObservedElapsedTicks,
    string ProcessDurationTicks,
    string DispatchedAtUtc,
    string ObservedAtUtc,
    string ConfirmationVersion,
    string ConfirmationSha256,
    string ConfirmationExpiresAtUtc);
