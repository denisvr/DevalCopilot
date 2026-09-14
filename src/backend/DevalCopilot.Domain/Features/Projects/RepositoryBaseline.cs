namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// One immutable, truthfully-observed snapshot of a registered project's Git state —
/// append-only, never mutated or superseded in place. "Current" is a read-time query concept
/// (the greatest <see cref="BaselineNumber"/> for a project), never a special persisted flag,
/// so a future re-validation or per-run precondition capture can simply insert another row
/// without a schema change.
///
/// <para>
/// This is the registration-time trust snapshot only — proof that the repository is real and
/// observable. It is a distinct, narrower concept from the future per-run precondition baseline
/// (remote identity, instruction-file hashes, tool versions, verification commands) captured
/// immediately before a run creates its worktree; the two are not expected to share a schema.
/// </para>
/// </summary>
public sealed class RepositoryBaseline
{
    private RepositoryBaseline()
    {
    }

    /// <summary>
    /// The exact, non-obvious field-presence invariant, derived from real Git semantics:
    /// <list type="table">
    /// <item><description><see cref="RepositoryHeadState.OnBranch"/>: both <paramref name="branchName"/>
    /// and <paramref name="headCommitSha"/> required.</description></item>
    /// <item><description><see cref="RepositoryHeadState.Unborn"/>: <paramref name="branchName"/>
    /// required (the symbolic ref still resolves to a name), <paramref name="headCommitSha"/>
    /// forbidden (no commit exists yet).</description></item>
    /// <item><description><see cref="RepositoryHeadState.Detached"/>: <paramref name="branchName"/>
    /// forbidden, <paramref name="headCommitSha"/> required (detaching requires an existing
    /// commit to point at).</description></item>
    /// </list>
    /// </summary>
    public static RepositoryBaseline Capture(
        Guid id,
        Guid projectId,
        int baselineNumber,
        DateTimeOffset observedAtUtc,
        RepositoryHeadState headState,
        string? branchName,
        string? headCommitSha,
        bool isDirty)
    {
        if (baselineNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(baselineNumber));
        }

        var branchNameRequired = headState != RepositoryHeadState.Detached;
        if (branchNameRequired && string.IsNullOrWhiteSpace(branchName))
        {
            throw new ArgumentException($"A {headState} baseline requires a branch name.", nameof(branchName));
        }

        if (!branchNameRequired && branchName is not null)
        {
            throw new ArgumentException("A Detached baseline must not carry a branch name.", nameof(branchName));
        }

        var headCommitShaRequired = headState != RepositoryHeadState.Unborn;
        if (headCommitShaRequired && string.IsNullOrWhiteSpace(headCommitSha))
        {
            throw new ArgumentException($"A {headState} baseline requires a HEAD commit SHA.", nameof(headCommitSha));
        }

        if (!headCommitShaRequired && headCommitSha is not null)
        {
            throw new ArgumentException("An Unborn baseline must not carry a HEAD commit SHA.", nameof(headCommitSha));
        }

        return new RepositoryBaseline
        {
            Id = id,
            ProjectId = projectId,
            BaselineNumber = baselineNumber,
            ObservedAtUtc = observedAtUtc,
            HeadState = headState,
            BranchName = branchName,
            HeadCommitSha = headCommitSha,
            IsDirty = isDirty,
        };
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>Per-project monotonic sequence reserved via <see cref="Project.ReserveBaselineNumber"/>.
    /// The unique <c>(ProjectId, BaselineNumber)</c> constraint, not this value alone, is what
    /// guarantees ordering is unambiguous even under a future concurrent revalidation.</summary>
    public int BaselineNumber { get; private set; }

    /// <summary>Display metadata only — never used to determine which baseline is current.</summary>
    public DateTimeOffset ObservedAtUtc { get; private set; }

    public RepositoryHeadState HeadState { get; private set; }

    public string? BranchName { get; private set; }

    public string? HeadCommitSha { get; private set; }

    public bool IsDirty { get; private set; }
}
