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

    /// <summary>The execution was claimed as a step of an authorized local execution sequence (ADR-0060) whose authority no longer permitted
    /// dispatch. No process ran and no outcome, exit code or completion fingerprint exists.</summary>
    LocalExecutionSequenceDispatchRefused = 7,
}
