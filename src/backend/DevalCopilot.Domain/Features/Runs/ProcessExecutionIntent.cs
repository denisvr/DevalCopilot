namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The immutable, non-secret description of one child process a Process attempt commits to
/// running, durable before any external execution starts. Deliberately excludes environment
/// variables entirely — this slice never persists names or values, so a reconstructed request
/// always carries an empty environment.
/// </summary>
public sealed record ProcessExecutionIntent(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string ApprovedRoot,
    TimeSpan Timeout,
    int MaxBytesPerStream,
    int MaxTotalCapturedBytes);
