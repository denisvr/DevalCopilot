namespace DevalCopilot.Api.Features.Runs.AuthorizeReviewCorrectionWithGuidance;

/// <summary>The optional human guidance for one authorized review correction. Bounded and
/// normalized by the server; it is advisory context, never an instruction to the host.</summary>
public sealed record AuthorizeReviewCorrectionWithGuidanceRequest(string Guidance);
