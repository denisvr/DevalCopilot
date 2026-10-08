namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// What a bounded look at the reference-lock namespace of one local-commit transaction proves: the owned branch reference lock and
/// the HEAD lock of the proven linked-worktree administrative directory, the two locks a prepared reference transaction takes. Only
/// <see cref="Clear"/> is positive evidence. The first value is deliberately <see cref="Unproven"/>, so a default or historical
/// inspection shape can never imply that the namespace is clear.
/// </summary>
public enum LocalCommitReferenceLockState
{
    /// <summary>The namespace could not be proven either way: a missing, unreadable, redirected or unrepresentable path.</summary>
    Unproven = 0,

    /// <summary>Both locks were positively observed absent, through a path proven free of redirection.</summary>
    Clear = 1,

    /// <summary>At least one of the locks exists. A pathname, its bytes, a process id or its age are never operation ownership, so
    /// it is never adopted, removed or renamed, and it keeps the operation and workspace under attention.</summary>
    Present = 2,
}
