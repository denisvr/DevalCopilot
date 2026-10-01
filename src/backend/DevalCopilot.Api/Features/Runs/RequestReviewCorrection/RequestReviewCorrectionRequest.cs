namespace DevalCopilot.Api.Features.Runs.RequestReviewCorrection;

/// <summary>The completed changes-requested review to correct and optional direct human guidance. The server
/// normalizes and bounds the guidance; it is advisory clarification of the review findings, never an instruction
/// to the host, and is available only within the ordinary correction budget. Omitted or null means no guidance.</summary>
public sealed record RequestReviewCorrectionRequest(Guid ImplementationReviewAttemptId, string? Guidance = null);
