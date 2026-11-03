using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The launch identities the admission boundary recognises: the owner-approved tuples (<see cref="ApprovedLaunchSet"/>) and the one
/// owned root all destinations must lie in. Until an approved set exists nothing is recognised, so every launch is refused. The
/// launcher's own Git is recognised as the approved absolute Git file and nothing else. The host's Git commands and the two role
/// invocations are recognised only after the target judgement accepted the host's own readiness evidence AND that evidence named
/// exactly the approved tuples, so a provider or Git that discovery resolved anywhere else leaves those shapes unlisted. Revalidation
/// of the files themselves happens in the admission, before each start.
/// </summary>
public sealed class JudgedTargets(ApprovedLaunchSet? approved = null, OwnedDestinations? destinations = null)
{
    private readonly object _gate = new();
    private ApprovedLaunchSet? _approved = approved;
    private volatile bool _accepted;

    public OwnedDestinations Destinations { get; } = destinations ?? new OwnedDestinations();

    public ApprovedLaunchSet? Approved
    {
        get
        {
            lock (_gate)
            {
                return _approved;
            }
        }
    }

    public bool IsPinned => _accepted;

    /// <summary>Sets the approved launches once, before the first launch. A later call changes nothing and returns false.</summary>
    public bool Approve(ApprovedLaunchSet set)
    {
        lock (_gate)
        {
            if (_approved is not null)
            {
                return false;
            }

            _approved = set;
            return true;
        }
    }

    /// <summary>Accepts the host's readiness evidence only when each of the three facts is exactly its approved tuple. Returns whether
    /// it did; a mismatch leaves every host Git and role shape unlisted.</summary>
    public bool Pin(ProviderTargetFact git, ProviderTargetFact codex, ProviderTargetFact claude)
    {
        var set = Approved;
        _accepted = set is not null
            && git.Capability == Capability.Git && codex.Capability == Capability.CodexCli
            && claude.Capability == Capability.ClaudeCli
            && set.Matches(git) && set.Matches(codex) && set.Matches(claude);
        return _accepted;
    }

    /// <summary>The approved absolute Git file the launcher runs, or null before the approval exists.</summary>
    public string? LauncherGitPath => Approved?.GitLaunch.Executable.Path;

    /// <summary>Verifies again, immediately before a start, every approved file the request launches. A refusal when nothing is
    /// approved at all.</summary>
    public string? Revalidate(ProcessShape shape) => Approved is { } set ? set.Revalidate(shape) : "IdentityUnapproved";

    /// <summary>The launcher's own Git: the approved absolute Git file, never a name resolved through PATH.</summary>
    public bool IsLauncherGit(ProcessShape shape) => Approved?.GitLaunch.Executable.IsPath(shape.ExecutablePath) == true;

    /// <summary>A command of the host's Git: the approved Git file, after the judgement accepted the readiness evidence.</summary>
    public bool IsHostGit(ProcessShape shape) => _accepted && IsLauncherGit(shape);

    /// <summary>The approved launch of the Codex role: its executable and, for a script launch, the script as the first argument.
    /// Returns the argument offset of the role vector (0 or 1), or null when the shape is not that launch.</summary>
    public int? CodexRoleOffset(ProcessShape shape) =>
        _accepted && Approved is { } set ? RoleOffset(set.CodexLaunch, shape) : null;

    /// <summary>The same for the Claude role.</summary>
    public int? ClaudeRoleOffset(ProcessShape shape) =>
        _accepted && Approved is { } set ? RoleOffset(set.ClaudeLaunch, shape) : null;

    private static int? RoleOffset(ApprovedLaunch launch, ProcessShape shape)
    {
        if (!launch.Executable.IsPath(shape.ExecutablePath))
        {
            return null;
        }

        if (launch.Script is null)
        {
            return 0;
        }

        return shape.Arguments.Count > 0 && launch.Script.IsPath(shape.Arguments[0]) ? 1 : null;
    }
}
