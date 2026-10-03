namespace DevalCopilot.Api.Features.Runs.RequestDiagnosisCorrection;

/// <summary>The completed verification diagnosis whose findings are to be corrected and optional direct human guidance. The
/// server normalizes and bounds the guidance; it is advisory clarification of the diagnosis findings, never an instruction to
/// the host, and is available only within the shared correction allowance. Omitted or null means no guidance. There is no
/// authorization input for this source.</summary>
public sealed record RequestDiagnosisCorrectionRequest(Guid VerificationDiagnosisAttemptId, string? Guidance = null);
