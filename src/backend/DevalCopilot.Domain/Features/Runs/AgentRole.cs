namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// The closed set of real collaboration roles an <see cref="Attempt"/> of <see cref="AttemptKind.Agent"/>
/// can occupy. Appended, never renumbered, as a slice adds a real, proven role.
/// </summary>
public enum AgentRole
{
    Planner = 0,

    /// <summary>Claude Code reviewing a real, current Codex Proposal against the same run,
    /// isolated workspace, checkpoint, and fresh Git fingerprint. Never resolution, revision, or
    /// execution. ReviewCorrection responses to changes-requested reviews belong to the
    /// Implementer role, not this read-only reviewer role.</summary>
    CriticalReviewer = 1,

    /// <summary>Codex resolving a real, current Claude critical-review Challenge set against the
    /// same run, isolated workspace, checkpoint, and fresh Git fingerprint — explicitly deciding
    /// every Challenge and emitting one revised Proposal. Never execution or later review stages;
    /// those remain deferred.</summary>
    Resolver = 2,

    /// <summary>An Implementer agent implementing a real, resolved plan (an accepted original Proposal or
    /// a resolved revised Proposal) inside the owned worktree, against the same run, isolated
    /// workspace, and starting checkpoint. Real, evidenced source mutation is expected and
    /// required for success — never a read-only role. Automatic verification, code review, and
    /// publication remain deferred to later slices.</summary>
    Implementer = 3,

    /// <summary>A CodeReviewer agent reviewing a real, complete implementation result — the exact
    /// Execution report, its immutable result checkpoint, and the exact ordered set of currently
    /// enabled verification-command executions bound to that same checkpoint, every one of them
    /// Passed. Read-only: this role never mutates the worktree, runs Git, runs a verification
    /// command itself, or has network/process capability. A provider's own revision/correction
    /// response to a changes-requested review is owned by the Implementer role.</summary>
    CodeReviewer = 4,
}
