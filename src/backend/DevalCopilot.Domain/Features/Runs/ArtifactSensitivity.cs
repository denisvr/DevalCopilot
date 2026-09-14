namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The redaction/sensitivity classification <c>docs/architecture/data-and-recovery.md</c>
/// requires on every artifact record. This is the only value this slice's process-output
/// producer ever sets — every capture already passes through the Infrastructure streaming
/// redactor before a byte is written — but the classification exists as required metadata so a
/// future producer of genuinely unredacted or higher-sensitivity artifacts has a distinct value
/// to set without a schema change.
/// </summary>
public enum ArtifactSensitivity
{
    /// <summary>
    /// Passed through the bounded, best-effort streaming redactor — a fixed, documented list of
    /// secret-shaped patterns, applied before any byte reaches disk — before this artifact was
    /// written. This is a truthful statement of what was *attempted*, never a claim that the
    /// content is proven free of secrets: an unknown or custom secret format, or a match longer
    /// than the redactor's supported bound, is not detected. Treat this value as "reduced risk,"
    /// not "verified safe."
    /// </summary>
    RedactedBestEffort = 0,
}
