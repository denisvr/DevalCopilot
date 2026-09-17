using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Ports;

/// <summary>
/// The outcome of one host-level discovery-and-version-probe attempt for a single capability.
/// Never carries raw process output — only a closed reason and, on success, a resolved path and
/// a parsed version string.
///
/// <para>
/// Closed by construction: the only way to obtain an instance is <see cref="DirectExecutableSuccess"/>,
/// <see cref="NodeScriptSuccess"/>, or <see cref="Failed"/>, each of which validates its own
/// required fields before returning. A caller cannot build a success result missing its
/// executable path, launch kind, or version, and cannot build a failure carrying
/// <see cref="CapabilityProbeReason.None"/> or a snapshot-lifecycle-only reason an adapter never
/// produces.
/// </para>
/// </summary>
public sealed class ToolDiscoveryResult
{
    private ToolDiscoveryResult(
        CapabilityProbeReason reason,
        CapabilityLaunchKind? launchKind,
        string? resolvedExecutablePath,
        string? resolvedScriptPath,
        string? version)
    {
        Reason = reason;
        LaunchKind = launchKind;
        ResolvedExecutablePath = resolvedExecutablePath;
        ResolvedScriptPath = resolvedScriptPath;
        Version = version;
    }

    /// <summary><see cref="CapabilityProbeReason.None"/> on success. Never
    /// <see cref="CapabilityProbeReason.NeverProbed"/> or
    /// <see cref="CapabilityProbeReason.ProbeInterruptedByRestart"/> — those describe snapshot
    /// lifecycle states an adapter never produces.</summary>
    public CapabilityProbeReason Reason { get; }

    /// <summary>Only set when <see cref="Reason"/> is <see cref="CapabilityProbeReason.None"/>.</summary>
    public CapabilityLaunchKind? LaunchKind { get; }

    /// <summary>Only set when <see cref="Reason"/> is <see cref="CapabilityProbeReason.None"/>.
    /// For <see cref="CapabilityLaunchKind.NodeScript"/>, this is the <c>node.exe</c> path that
    /// was actually invoked, never the JavaScript entrypoint.</summary>
    public string? ResolvedExecutablePath { get; }

    /// <summary>Only set when <see cref="Reason"/> is <see cref="CapabilityProbeReason.None"/> and
    /// <see cref="LaunchKind"/> is <see cref="CapabilityLaunchKind.NodeScript"/>.</summary>
    public string? ResolvedScriptPath { get; }

    /// <summary>Only set when <see cref="Reason"/> is <see cref="CapabilityProbeReason.None"/>.</summary>
    public string? Version { get; }

    public static ToolDiscoveryResult DirectExecutableSuccess(string resolvedExecutablePath, string version)
    {
        RequireAbsolutePath(resolvedExecutablePath, nameof(resolvedExecutablePath));
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        return new ToolDiscoveryResult(
            CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable, resolvedExecutablePath, null, version);
    }

    public static ToolDiscoveryResult NodeScriptSuccess(string resolvedNodeExecutablePath, string resolvedScriptPath, string version)
    {
        RequireAbsolutePath(resolvedNodeExecutablePath, nameof(resolvedNodeExecutablePath));
        RequireAbsolutePath(resolvedScriptPath, nameof(resolvedScriptPath));
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        return new ToolDiscoveryResult(
            CapabilityProbeReason.None, CapabilityLaunchKind.NodeScript, resolvedNodeExecutablePath, resolvedScriptPath, version);
    }

    public static ToolDiscoveryResult Failed(CapabilityProbeReason reason)
    {
        if (!IsValidFailureReason(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a valid probe-failure reason.");
        }

        return new ToolDiscoveryResult(reason, null, null, null, null);
    }

    /// <summary>Exposed so the command handler can defensively re-validate a result it receives
    /// through the port, rather than trusting that every current and future
    /// <c>IToolDiscoveryAdapter</c> implementation only ever goes through the factories
    /// above.</summary>
    public static bool IsValidFailureReason(CapabilityProbeReason reason) =>
        Enum.IsDefined(reason) &&
        reason is not (CapabilityProbeReason.None or CapabilityProbeReason.NeverProbed or CapabilityProbeReason.ProbeInterruptedByRestart);

    private static void RequireAbsolutePath(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{paramName}' must be a non-blank, absolute path.", paramName);
        }
    }
}
