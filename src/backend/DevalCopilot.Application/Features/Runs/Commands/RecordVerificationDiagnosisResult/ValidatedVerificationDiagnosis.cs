using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisResult;

/// <summary>
/// A Codex verification-diagnosis final response that has already passed protocol/schema validation — a strict
/// discriminated Findings-or-Escalation shape, reduced to exactly the bounded strings the collaboration messages need. The
/// private constructor and the two factories are the only way to construct it: <see cref="IsEscalation"/>,
/// <see cref="Findings"/>, and <see cref="Escalation"/> can never disagree. Domain still independently validates every field
/// when each message is constructed, and the recording handler re-verifies cardinality — this is not the only defense. There
/// is deliberately no approval shape.
/// </summary>
public sealed class ValidatedVerificationDiagnosis
{
    private ValidatedVerificationDiagnosis(
        string summary, IReadOnlyList<ValidatedReviewFinding> findings, ValidatedDiagnosisEscalation? escalation)
    {
        Summary = summary;
        Findings = findings;
        Escalation = escalation;
    }

    public static ValidatedVerificationDiagnosis CreateFindings(string summary, IReadOnlyList<ValidatedReviewFinding> findings) =>
        new(summary, findings.ToArray(), null);

    public static ValidatedVerificationDiagnosis CreateEscalation(string summary, ValidatedDiagnosisEscalation escalation) =>
        new(summary, [], escalation);

    public bool IsEscalation => Escalation is not null;

    public string Summary { get; }

    /// <summary>Empty for an escalation; one to ten entries otherwise (re-verified by the recording handler).</summary>
    public IReadOnlyList<ValidatedReviewFinding> Findings { get; }

    public ValidatedDiagnosisEscalation? Escalation { get; }
}
