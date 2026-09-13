using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Ports;

/// <summary>
/// The outcome of one host-level discovery-and-version-probe attempt for a single capability.
/// Never carries raw process output — only a closed reason and, on success, a resolved path
/// and a parsed version string.
/// </summary>
public sealed record ToolDiscoveryResult
{
    /// <summary><see cref="CapabilityProbeReason.None"/> on success. Never
    /// <see cref="CapabilityProbeReason.NeverProbed"/> or
    /// <see cref="CapabilityProbeReason.ProbeInterruptedByRestart"/> — those describe snapshot
    /// lifecycle states an adapter never produces.</summary>
    public required CapabilityProbeReason Reason { get; init; }

    /// <summary>Only set when <see cref="Reason"/> is <see cref="CapabilityProbeReason.None"/>.</summary>
    public string? ResolvedExecutablePath { get; init; }

    /// <summary>Only set when <see cref="Reason"/> is <see cref="CapabilityProbeReason.None"/>.</summary>
    public string? Version { get; init; }
}
