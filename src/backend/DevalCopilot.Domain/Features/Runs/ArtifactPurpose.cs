namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// What an <see cref="Artifact"/> durably captured. Closed and process-output-only for this
/// slice; future producers (Git diffs, screenshots, CI logs) extend this enum rather than
/// introducing a parallel record type.
/// </summary>
public enum ArtifactPurpose
{
    ProcessStandardOutput = 0,
    ProcessStandardError = 1,
}
