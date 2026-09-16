namespace DevalCopilot.Domain.Features.Projects;

/// <summary>How the child process itself ended, distinct from the source-evidence verdict.</summary>
public enum VerificationExecutionOutcome
{
    Exited = 0,
    TimedOut = 1,
    Cancelled = 2,
    Failed = 3,
}
