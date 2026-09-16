namespace DevalCopilot.Domain.Features.Projects;

/// <summary>States whether verification-output completeness is known.</summary>
public enum VerificationOutputCaptureOutcome
{
    /// <summary>The same host that captured the output durably knows the truncation result.</summary>
    CapturedWithKnownTruncation = 0,

    /// <summary>The output was recovered after host interruption, so truncation is unknown.</summary>
    RecoveredAfterHostInterruption = 1,
}
