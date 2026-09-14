namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// A truthful classification of how an artifact's capture ended. Never invents "complete" for
/// output that was only ever partially observed.
/// </summary>
public enum ArtifactCaptureOutcome
{
    /// <summary>The owning attempt reached a terminal result in the same host session that
    /// captured this output.</summary>
    Captured = 0,

    /// <summary>The owning host process ended before recording a terminal result; this artifact
    /// was sealed and imported by restart reconciliation from whatever was captured up to that
    /// point. Never presented as a complete capture.</summary>
    PartialHostInterrupted = 1,
}
