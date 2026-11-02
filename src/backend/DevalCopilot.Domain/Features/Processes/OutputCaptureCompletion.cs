namespace DevalCopilot.Domain.Features.Processes;

/// <summary>
/// ADR-0072: how the host's reading of one redirected output stream (stdout or stderr) of a child process ended, recorded
/// separately from the process outcome, its physical termination, its duration and byte-cap truncation. It says only whether
/// the host observed the stream's end; it never says that every emitted byte was retained (a truncated stream can be
/// <see cref="Complete"/>) and never proves that any descendant process stopped. Unknown is the default, so a value that was
/// never established is never read as complete.
/// </summary>
public enum OutputCaptureCompletion
{
    /// <summary>The host could not establish how the stream ended, including when its writer could not be proved closed.</summary>
    Unknown = 0,

    /// <summary>The stream reached end-of-file and its accepted bytes were finalized.</summary>
    Complete = 1,

    /// <summary>The stream had not ended when the bounded I/O completion window expired; the read was cancelled and the
    /// writer closed, so whatever was accepted before is partial.</summary>
    DeadlineExceeded = 2,

    /// <summary>Reading the stream or writing its accepted bytes failed before end-of-file.</summary>
    Faulted = 3,
}
