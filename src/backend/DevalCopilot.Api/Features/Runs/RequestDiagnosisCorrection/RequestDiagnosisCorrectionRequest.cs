namespace DevalCopilot.Api.Features.Runs.RequestDiagnosisCorrection;

/// <summary>The completed verification diagnosis whose findings are to be corrected. There is no guidance or authorization
/// input for this source.</summary>
public sealed record RequestDiagnosisCorrectionRequest(Guid VerificationDiagnosisAttemptId);
