namespace DevalCopilot.Domain.Features.Runs;

/// <summary>The fixed kind of one step of a local execution sequence (ADR-0060). Verification steps are project verification executions;
/// every other kind is an Agent attempt composed from an existing manual claim command.</summary>
public enum LocalExecutionStepKind
{
    Implementation = 0,
    Verification = 1,
    CodeReview = 2,
    VerificationDiagnosis = 3,
    ReviewCorrection = 4,
    DiagnosisCorrection = 5,
}
