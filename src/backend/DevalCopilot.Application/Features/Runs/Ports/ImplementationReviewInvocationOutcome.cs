namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// A closed, process-level classification only — never a statement about whether the final
/// response is a valid Approved-or-ChangesRequested implementation review. That is a separate,
/// later validation step over the sealed artifact this invocation produces. Mirrors
/// <see cref="ChallengeResolutionInvocationOutcome"/> exactly.
/// </summary>
public enum ImplementationReviewInvocationOutcome
{
    /// <summary>The process ran to completion and exited zero. Says nothing about whether its
    /// final-response file contains a valid review.</summary>
    Exited,

    /// <summary>The manifest could not be safely re-read, the launch target no longer exists or
    /// no longer matches its accepted shape, the process could not be started, exited non-zero,
    /// timed out, or was cancelled.</summary>
    Failed,
}
