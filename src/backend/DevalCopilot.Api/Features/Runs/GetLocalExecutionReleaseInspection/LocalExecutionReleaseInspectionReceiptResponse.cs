namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionReleaseInspection;

/// <summary>
/// The one proved Human quarantine release (ADR-0067): the release identity, its UTC time and the canonical positive Int64 sequence
/// number of the one Human run event; the proved sequence, step, operation and pinned source; the operation's unchanged failed result with
/// its process outcome; and the distinct physical termination facts of the original host's observation (an exact Int32 exit code and
/// nonnegative Int64 tick strings; zero is valid). Physical termination is evidence about the invoked process only, never every
/// descendant, and it does not change the recorded result. It carries no nonce, host launch identity, ticket, process identifier, path,
/// executable, argument, output, confirmation or action candidate.
/// </summary>
public sealed record LocalExecutionReleaseInspectionReceiptResponse(
    Guid ReleaseId,
    string ReleasedAtUtc,
    string EventSequence,
    Guid SequenceId,
    string SequenceState,
    string AdmissionMode,
    string StepKind,
    int Ordinal,
    int PassNumber,
    string OperationFamily,
    Guid OperationId,
    Guid SourceCheckpointId,
    string SourceFingerprintSha256,
    string OperationStatus,
    string ProcessOutcome,
    int? SemanticExitCode,
    string ProcessDurationTicks,
    Guid WitnessId,
    Guid ObservationId,
    string DispatchedAtUtc,
    string RecordedAtUtc,
    int PhysicalExitCode,
    string ObservedElapsedTicks);
