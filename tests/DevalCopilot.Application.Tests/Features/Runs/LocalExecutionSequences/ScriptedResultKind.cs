namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>The result one claimed step of a driven local execution cycle records, through the production result recorders.</summary>
public enum ScriptedResultKind
{
    /// <summary>The implementation recorded as Implemented with its ExecutionReport and result checkpoint, after a clean exit.</summary>
    Implemented,

    /// <summary>A correction recorded as CorrectionApplied with one RevisionResponse per finding and the report chain.</summary>
    CorrectionApplied,

    /// <summary>A verification execution that exited zero.</summary>
    VerificationPassed,

    /// <summary>A verification execution that exited non-zero with sealed stdout and stderr.</summary>
    VerificationFailed,

    /// <summary>A verification execution that timed out (not diagnosable, no exit evidence).</summary>
    VerificationTimedOut,

    /// <summary>A verification execution that exited zero but whose completion fingerprint no longer matches (SourceChanged).</summary>
    VerificationSourceChanged,

    /// <summary>A CodeReview recorded as ReviewApproved with its approval message.</summary>
    ReviewApproved,

    /// <summary>A CodeReview recorded as ReviewChangesRequested with findings.</summary>
    ReviewChangesRequested,

    /// <summary>A VerificationDiagnosis recorded as DiagnosisFindingsRecorded with findings.</summary>
    DiagnosisFindings,

    /// <summary>A VerificationDiagnosis recorded as DiagnosisEscalated with its provider Escalation.</summary>
    DiagnosisEscalated,

    /// <summary>An Agent attempt recorded as ProviderInvocationFailed with a clean non-zero exit proof.</summary>
    ProviderFailedWithExit,

    /// <summary>An Agent attempt recorded as ProviderInvocationFailed with NO process exit evidence (a timeout/cancel/adapter failure).</summary>
    ProviderFailedNoExit,

    /// <summary>An Agent attempt recorded as InvalidStructuredOutput after a clean exit.</summary>
    InvalidOutput,

    /// <summary>The operation is dispatched and left running: no result is ever recorded.</summary>
    LeaveRunning,

    /// <summary>The operation is claimed and left undispatched.</summary>
    LeaveUndispatched,
}
