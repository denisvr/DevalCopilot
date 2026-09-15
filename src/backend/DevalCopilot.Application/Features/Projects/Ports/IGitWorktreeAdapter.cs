namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// Creates and inspects tool-owned Git worktrees against an already-registered repository.
/// Every invocation is fixed, bounded, argument-list-only, and reuses the same hardening
/// discipline as <see cref="IGitRepositoryInspector"/> — never a shell, never ambient repository
/// config left free to run a helper. Read-only except for exactly one command
/// (<see cref="CreateAsync"/>'s <c>worktree add</c>), which only ever creates a new branch and a
/// new linked worktree; it never touches the main checkout's <c>HEAD</c>, index, or any
/// pre-existing ref. See ADR-0008.
/// </summary>
public interface IGitWorktreeAdapter
{
    /// <summary>Runs <c>git worktree add -b &lt;branchName&gt; &lt;workspacePath&gt;
    /// &lt;resolvedCommitSha&gt;</c> with working directory <paramref name="mainRepositoryPath"/>.
    /// <paramref name="resolvedCommitSha"/> must already be a resolved commit SHA, never a
    /// branch name or <c>HEAD</c> — the caller pins the exact starting point.</summary>
    Task<GitWorktreeCreationResult> CreateAsync(
        string mainRepositoryPath,
        string workspacePath,
        string branchName,
        string resolvedCommitSha,
        CancellationToken cancellationToken);

    /// <summary>Discovers the newly created workspace's own real Git administrative directory
    /// (via <c>rev-parse --git-dir</c>, run from inside the workspace) and cross-validates its
    /// git-common-dir against the one <paramref name="mainRepositoryPath"/> itself reports —
    /// never assumed to be <c>&lt;main-repo&gt;\.git\worktrees\&lt;name&gt;</c> by construction.</summary>
    Task<GitWorktreeAdministrativeDirectoryResult> ResolveAdministrativeDirectoryAsync(
        string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken);

    /// <summary>Read-only <c>git rev-parse HEAD</c> run from inside an existing workspace — used
    /// by reconciliation to detect content that diverged from the recorded source commit.</summary>
    Task<GitWorktreeHeadResult> GetHeadCommitShaAsync(string workspacePath, CancellationToken cancellationToken);

    /// <summary>Read-only <c>git worktree list --porcelain</c> run against the main repository,
    /// with the path comparison against <paramref name="workspacePath"/> performed here (never
    /// by the Application-layer caller, which must not depend on <c>Path.*</c>) — used by
    /// reconciliation to cross-check Git's own view of registered worktrees.</summary>
    Task<GitWorktreeRegistrationResult> IsRegisteredAsync(
        string mainRepositoryPath, string workspacePath, CancellationToken cancellationToken);
}

public enum GitWorktreeCreationOutcome
{
    Success,
    PathAlreadyExists,

    /// <summary>The computed workspace path is equal to, nested inside, or an ancestor of the
    /// main repository path — checked by path-segment-aware containment, before any Git
    /// invocation or filesystem mutation. Never carries either path in its own name or any
    /// associated data; both remain internal to the failed check.</summary>
    WorkspaceOverlapsMainRepository,

    GitUnavailable,
    GitInvocationFailed,
    GitInvocationTimedOut,
}

public sealed record GitWorktreeCreationResult(GitWorktreeCreationOutcome Outcome);

public enum GitWorktreeAdministrativeDirectoryOutcome
{
    Resolved,
    NotAWorktree,
    GitInvocationFailed,
    GitInvocationTimedOut,
}

/// <paramref name="AdministrativeDirectory"/> and <paramref name="CommonDirectory"/> are set
/// only when <paramref name="Outcome"/> is
/// <see cref="GitWorktreeAdministrativeDirectoryOutcome.Resolved"/>.
public sealed record GitWorktreeAdministrativeDirectoryResult(
    GitWorktreeAdministrativeDirectoryOutcome Outcome, string? AdministrativeDirectory, string? CommonDirectory);

public enum GitWorktreeHeadOutcome
{
    Resolved,
    GitInvocationFailed,
    GitInvocationTimedOut,
}

/// <paramref name="CommitSha"/> is set only when <paramref name="Outcome"/> is
/// <see cref="GitWorktreeHeadOutcome.Resolved"/>.
public sealed record GitWorktreeHeadResult(GitWorktreeHeadOutcome Outcome, string? CommitSha);

public enum GitWorktreeRegistrationOutcome
{
    Registered,
    NotRegistered,
    GitInvocationFailed,
    GitInvocationTimedOut,
}

public sealed record GitWorktreeRegistrationResult(GitWorktreeRegistrationOutcome Outcome);
