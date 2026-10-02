namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>A closed, process-level classification only — never a statement about whether the final response is a valid
/// diagnosis.</summary>
public enum VerificationDiagnosisInvocationOutcome
{
    /// <summary>The process ran to completion and exited zero.</summary>
    Exited,

    /// <summary>The manifest could not be re-read, the launch target no longer matches, or the process could not start,
    /// exited non-zero, timed out, or was cancelled.</summary>
    Failed,
}
