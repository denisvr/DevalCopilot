namespace DevalCopilot.Domain.Features.Projects;

/// <summary>Lifecycle of one immutable, checkpoint-bound local verification execution.</summary>
public enum VerificationExecutionStatus
{
    Running = 0,
    Passed = 1,
    Failed = 2,
    TimedOut = 3,
    Cancelled = 4,
    Interrupted = 5,
    SourceChanged = 6,
}
