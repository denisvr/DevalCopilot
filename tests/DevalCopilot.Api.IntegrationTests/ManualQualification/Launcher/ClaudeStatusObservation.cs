namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// One status observation reduced to closed values: the context, the physical outcome, the exit code, the duration, the I/O
/// completion facts and what was reported. The configuration-directory fingerprint exists only so the two observations can be
/// compared in memory; it is never rendered. Nothing here carries output, an account, a path or an environment value.
/// </summary>
public sealed record ClaudeStatusObservation(
    string Context,
    ClaudeStatusPhysical Physical,
    int? ExitCode,
    long? DurationMilliseconds,
    string? StandardOutputCompletion,
    string? StandardErrorCompletion,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    ClaudeStatusState State,
    string AuthMethod,
    string? ConfigDirectoryFingerprint)
{
    public const string Ordinary = "ordinary";
    public const string Filtered = "filtered";

    /// <summary>Both output streams ended (and no input was redirected): the report may be trusted as complete.</summary>
    public bool IoComplete => StandardOutputCompletion == "Complete" && StandardErrorCompletion == "Complete";

    /// <summary>A completed observation: exited, complete I/O, untruncated output and a coherent report.</summary>
    public bool IsCoherent =>
        Physical == ClaudeStatusPhysical.Exited && IoComplete && !StandardOutputTruncated && !StandardErrorTruncated
        && State != ClaudeStatusState.Unknown;

    public static ClaudeStatusObservation Unobserved(string context, ClaudeStatusPhysical physical) =>
        new(context, physical, null, null, null, null, false, false, ClaudeStatusState.Unknown, "unknown", null);
}
