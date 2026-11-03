using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The complete set of launch tuples the owner approved for one lot B session. Git, Codex and Claude are required; a Node component
/// is required exactly when a provider is a Node-hosted script launch, and the remaining catalog capabilities (dotnet, gh, docker)
/// are approved only if the owner lists them. Nothing outside the set may be probed or launched: a discovery probe, a Git command or a
/// role invocation is admitted only as one of these exact tuples, and every file of the tuple is verified again against its pinned
/// digest immediately before the start. The same approved absolute Git identity serves the launcher and the host.
/// </summary>
public sealed class ApprovedLaunchSet
{
    public const string Git = "git";
    public const string DotNet = "dotnet";
    public const string Node = "node";
    public const string GitHub = "gh";
    public const string Docker = "docker";
    public const string Codex = "codex";
    public const string Claude = "claude";

    public static readonly IReadOnlyList<string> CapabilityNames = [Git, DotNet, Node, GitHub, Docker, Codex, Claude];

    private readonly Dictionary<string, ApprovedLaunch> _byCapability;

    private ApprovedLaunchSet(Dictionary<string, ApprovedLaunch> byCapability)
    {
        _byCapability = byCapability;
    }

    public IReadOnlyCollection<ApprovedLaunch> Launches => _byCapability.Values;

    public ApprovedLaunch GitLaunch => _byCapability[Git];

    public ApprovedLaunch CodexLaunch => _byCapability[Codex];

    public ApprovedLaunch ClaudeLaunch => _byCapability[Claude];

    /// <summary>The set, or the closed refusal of the first structural problem. Structure only: no file is read here.</summary>
    public static (ApprovedLaunchSet? Set, string? Refusal) TryCreate(IReadOnlyList<ApprovedLaunch> launches)
    {
        var byCapability = new Dictionary<string, ApprovedLaunch>(StringComparer.Ordinal);
        foreach (var launch in launches)
        {
            if (!CapabilityNames.Contains(launch.Capability, StringComparer.Ordinal))
            {
                return (null, "ApprovedCapabilityUnknown");
            }

            if (!byCapability.TryAdd(launch.Capability, launch))
            {
                return (null, "ApprovedCapabilityDuplicated");
            }

            if (!IsExact(launch.Executable) || (launch.Script is { } script && !IsExact(script)))
            {
                return (null, "ApprovedFileNotExact");
            }

            if (launch.Script is not null && launch.Capability is not (Codex or Claude))
            {
                return (null, "ApprovedScriptNotPermitted");
            }
        }

        if (!byCapability.ContainsKey(Git) || !byCapability.ContainsKey(Codex) || !byCapability.ContainsKey(Claude))
        {
            return (null, "ApprovedProviderMissing");
        }

        var scripted = byCapability.Values.Where(launch => launch.Script is not null).ToArray();
        if (scripted.Length > 0)
        {
            if (!byCapability.TryGetValue(Node, out var node) || node.Script is not null)
            {
                return (null, "ApprovedNodeComponentMissing");
            }

            if (scripted.Any(launch =>
                !node.Executable.IsPath(launch.Executable.Path) || node.Executable.Sha256 != launch.Executable.Sha256))
            {
                return (null, "ApprovedNodeComponentDisagrees");
            }
        }

        return (new ApprovedLaunchSet(byCapability), null);
    }

    public ApprovedLaunch? Find(string capability) => _byCapability.GetValueOrDefault(capability);

    /// <summary>Verifies every approved file against its pinned digest. The closed refusal of the first failure, or null.</summary>
    public string? VerifyAll()
    {
        foreach (var launch in _byCapability.Values)
        {
            var refusal = launch.Executable.Verify() ?? launch.Script?.Verify();
            if (refusal is not null)
            {
                return refusal;
            }
        }

        return null;
    }

    /// <summary>True when the host's own readiness evidence for <paramref name="fact"/> is exactly the approved tuple of that
    /// capability: the same executable and, for a script launch, the same script, and no other launch kind.</summary>
    public bool Matches(ProviderTargetFact fact)
    {
        if (fact.Reason != CapabilityProbeReason.None || Name(fact.Capability) is not { } name || Find(name) is not { } approved)
        {
            return false;
        }

        if (!approved.Executable.IsPath(fact.ExecutablePath))
        {
            return false;
        }

        return approved.Script is null
            ? fact.ScriptPath is null && fact.LaunchKind == CapabilityLaunchKind.DirectExecutable
            : approved.Script.IsPath(fact.ScriptPath) && fact.LaunchKind == CapabilityLaunchKind.NodeScript;
    }

    /// <summary>Verifies again, immediately before dispatch, every approved file the request launches: its executable and, when its
    /// first argument is an approved script, that script. Returns the closed refusal, or null. A request that launches no approved
    /// executable is itself refused, so no unapproved file is ever reached through this path.</summary>
    public string? Revalidate(ProcessShape shape)
    {
        var launched = false;
        var verified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var launch in _byCapability.Values)
        {
            foreach (var file in new[] { launch.Executable, launch.Script }.OfType<ApprovedFile>())
            {
                var isExecutable = file.IsPath(shape.ExecutablePath);
                var isScript = shape.Arguments.Count > 0 && launch.Script == file && file.IsPath(shape.Arguments[0]);
                if (!isExecutable && !isScript)
                {
                    continue;
                }

                launched |= isExecutable;
                if (verified.Add(file.Path) && file.Verify() is { } refusal)
                {
                    return refusal;
                }
            }
        }

        return launched ? null : "IdentityUnapproved";
    }

    private static bool IsExact(ApprovedFile file) =>
        Path.IsPathFullyQualified(file.Path)
        && string.Equals(Path.GetFullPath(file.Path), file.Path, StringComparison.OrdinalIgnoreCase)
        && ApprovedFile.IsSha256(file.Sha256);

    private static string? Name(Capability capability) => capability switch
    {
        Capability.Git => Git,
        Capability.DotNetSdk => DotNet,
        Capability.Node => Node,
        Capability.GitHubCli => GitHub,
        Capability.Docker => Docker,
        Capability.CodexCli => Codex,
        Capability.ClaudeCli => Claude,
        _ => null,
    };
}
