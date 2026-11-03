using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The launch-context check of lot B, run inside bounded preparation after the manifest is verified and before any fixture, Git probe,
/// child process or slot exists. Every catalog capability is resolved by the production target resolver in this process's own
/// environment (the context the host will probe in), and each resolved launch is compared with the owner's manifest: a direct
/// executable must be the approved executable with no approved script, and a Node-hosted script must be the approved Node executable
/// and the approved script together (both paths), so a direct pin never matches a script resolution and the reverse. A target that is
/// missing from the machine but approved, found but not approved (it would be probed and refused as unlisted), ambiguous, or
/// different from the approval is refused with a closed code naming only the capability and the kind, never a path or an environment
/// value. The first refusal, in the catalog's order, is the answer. The runtime admission checks, which refuse each request again,
/// are unchanged and stay the control of every start.
/// </summary>
public static class LaunchContextCheck
{
    public const string ResolverUnavailableCode = "LaunchContextResolverUnavailable";

    /// <summary>The closed refusal code of the first capability whose resolved launch is not exactly the approval, or null when every
    /// capability agrees. Throws <see cref="OperationCanceledException"/> as soon as <paramref name="isExpired"/> is true, before the
    /// next resolution and again before an answer is used.</summary>
    public static string? Refusal(ApprovedLaunchSet approved, Func<Capability, LaunchResolution> resolve, Func<bool> isExpired)
    {
        foreach (var capability in Enum.GetValues<Capability>())
        {
            ThrowIfExpired(isExpired);
            var resolution = resolve(capability);
            ThrowIfExpired(isExpired);
            if (Compare(capability, resolution, approved) is { } refusal)
            {
                return refusal;
            }
        }

        return null;
    }

    private static string? Compare(Capability capability, LaunchResolution resolution, ApprovedLaunchSet approved)
    {
        if (ApprovedLaunchSet.NameOf(capability) is not { } name)
        {
            return ResolverUnavailableCode;
        }

        var launch = approved.Find(name);
        string Code(string kind) => $"LaunchContext{capability}{kind}";
        return resolution.Kind switch
        {
            LaunchResolutionKind.NotFound => launch is null ? null : Code("Missing"),
            LaunchResolutionKind.Ambiguous => Code("Ambiguous"),
            LaunchResolutionKind.Direct => launch is null
                ? Code("Extra")
                : launch.Script is null && launch.Executable.IsPath(resolution.ExecutablePath) ? null : Code("Mismatch"),
            LaunchResolutionKind.NodeScript => launch is null
                ? Code("Extra")
                : launch.Script is { } script && launch.Executable.IsPath(resolution.ExecutablePath) && script.IsPath(resolution.ScriptPath)
                    ? null
                    : Code("Mismatch"),
            _ => ResolverUnavailableCode,
        };
    }

    private static void ThrowIfExpired(Func<bool> isExpired)
    {
        if (isExpired())
        {
            throw new OperationCanceledException("The entry deadline elapsed.");
        }
    }
}
