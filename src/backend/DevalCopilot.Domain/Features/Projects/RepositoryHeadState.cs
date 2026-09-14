namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// The three mutually exclusive shapes a Git <c>HEAD</c> can take, derived from real Git
/// semantics rather than a simplified guess:
/// <list type="bullet">
/// <item><see cref="OnBranch"/> — a symbolic ref resolves to a branch name, and that branch has
/// at least one commit.</item>
/// <item><see cref="Unborn"/> — a symbolic ref still resolves to a branch name (e.g. a freshly
/// <c>git init</c>'d repository), but that branch has no commits yet.</item>
/// <item><see cref="Detached"/> — <c>HEAD</c> points directly at a commit, not at a branch ref.
/// This is only reachable when a commit already exists, since there is no way to detach onto
/// nothing.</item>
/// </list>
/// See <see cref="RepositoryBaseline"/> for the exact field-presence invariant each state
/// implies.
/// </summary>
public enum RepositoryHeadState
{
    OnBranch = 0,
    Unborn = 1,
    Detached = 2,
}
