namespace DevalCopilot.Infrastructure.Features.Runs;

internal enum LocalCommitRefCommitOutcome
{
    /// <summary>The exact <c>commit: ok</c> acknowledgement arrived, stdin was closed, the child exited with code zero, and neither
    /// stream exceeded its bound or carried unexpected bytes. It is evidence for the caller's own exact proofs, not a release proof.</summary>
    Acknowledged,

    /// <summary><c>commit</c> was attempted and the outcome cannot be proven by the protocol alone. It is never resent.</summary>
    Uncertain,

    /// <summary>The transaction was not in a state that permits <c>commit</c>; nothing was sent.</summary>
    NotAttempted,
}
