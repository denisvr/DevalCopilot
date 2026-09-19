namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// A closed, process-level classification only — never a statement about whether the final
/// response is a valid implementation report, and never a statement about whether the worktree
/// was mutated. Both are separate, later validation steps: parsing the sealed final-response
/// artifact, and independently re-reading fresh Git evidence after this invocation returns.
/// </summary>
public enum ImplementationInvocationOutcome
{
    /// <summary>The process ran to completion and exited zero. Says nothing about whether its
    /// final-response file contains a valid implementation report, and says nothing about
    /// whether the worktree was actually edited.</summary>
    Exited,

    /// <summary>The manifest could not be safely re-read, the launch target no longer exists or
    /// no longer matches its accepted shape, the process could not be started, exited non-zero,
    /// timed out, or was cancelled. The worktree may still have been mutated before this failure
    /// occurred — the caller always independently re-reads Git evidence after this outcome, never
    /// assumes it is safe to skip that check because the process failed.</summary>
    Failed,
}
