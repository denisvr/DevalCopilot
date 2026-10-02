namespace DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisDispatchRefusal;

/// <summary>The two dispatch-gate losses of a verification diagnosis that have no generic equivalent.</summary>
public enum VerificationDiagnosisDispatchRefusal
{
    /// <summary>Another diagnosis already completed successfully for the exact same report, checkpoint, and verification identity.</summary>
    InputAlreadyDiagnosed,

    /// <summary>The enabled set, a latest execution, the failed output, or the report chain the claim pinned changed.</summary>
    VerificationEvidenceChanged,
}
