namespace DevalCopilot.Api.Features.Runs.RequestDiagnosisCorrection;

public sealed record RequestDiagnosisCorrectionResponse(
    string Status,
    Guid? AttemptId,
    int? AttemptNumber,
    Guid? EscalationId,
    Guid? EscalationMessageId,
    long? LatestEventSequence);
