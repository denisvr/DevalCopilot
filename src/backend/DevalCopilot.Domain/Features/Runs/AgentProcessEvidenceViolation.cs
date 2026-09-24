namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// A closed reason why host-measured Agent child-process evidence cannot be recorded. Evaluated
/// by <see cref="AgentProcessEvidencePolicy"/> and enforced by every Agent completion transition
/// on <see cref="Attempt"/>.
/// </summary>
public enum AgentProcessEvidenceViolation
{
    /// <summary>The reported process outcome is not a defined <see cref="ProcessOutcome"/>.</summary>
    UndefinedOutcome = 0,

    /// <summary>An <see cref="ProcessOutcome.Exited"/> outcome was reported without an exit code.</summary>
    ExitedWithoutExitCode = 1,

    /// <summary>A timed-out or cancelled process was reported with an exit code; a killed process's
    /// exit code is never a meaningful signal.</summary>
    ExitCodeWithoutExited = 2,

    /// <summary>The host-measured duration is negative.</summary>
    NegativeDuration = 3,

    /// <summary>Evidence was supplied for an attempt that was never dispatched to its provider — no
    /// child process can exist for it.</summary>
    NotDispatched = 4,

    /// <summary>A semantic success outcome or <see cref="AgentOutcome.InvalidStructuredOutput"/>
    /// was requested without evidence that the child process exited with code zero.</summary>
    CleanExitRequired = 5,

    /// <summary>Evidence was supplied for an outcome that is, by its own Domain meaning, always
    /// detected immediately before the provider is ever invoked — <see cref="AgentOutcome.WorkspaceNoLongerEligible"/>
    /// or one of the "input already handled" race outcomes. No child process can exist for these
    /// regardless of the attempt's dispatch state.</summary>
    PreInvocationOutcomeCannotCarryEvidence = 6,
}
