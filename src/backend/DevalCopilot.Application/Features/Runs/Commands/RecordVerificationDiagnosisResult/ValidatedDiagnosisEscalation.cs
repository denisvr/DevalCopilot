namespace DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisResult;

/// <summary>The five bounded fields of an Escalation message, already validated against the content policy.</summary>
public sealed record ValidatedDiagnosisEscalation(
    string UnresolvedDecision, string Options, string Consequences, string Evidence, string RecommendedChoice);
