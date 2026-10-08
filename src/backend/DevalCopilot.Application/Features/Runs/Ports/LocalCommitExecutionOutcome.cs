namespace DevalCopilot.Application.Features.Runs.Ports;

public enum LocalCommitExecutionOutcome
{
    /// <summary>The recorded commit is the branch tip and the real index equals the recorded tree.</summary>
    Promoted,

    /// <summary>A precondition failed before any mutation, or the single ref update was refused with the ref, index and locks
    /// proven unchanged. The branch did not move.</summary>
    NotPromoted,

    /// <summary>The outcome cannot be proven either way; nothing may be retried or invented.</summary>
    Ambiguous,
}
