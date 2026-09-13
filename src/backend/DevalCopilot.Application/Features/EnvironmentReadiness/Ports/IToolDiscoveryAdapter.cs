using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Ports;

/// <summary>
/// Resolves and version-probes exactly one fixed catalog capability on this host. Infrastructure
/// owns the per-capability candidate executable names, fixed fallback directories, fixed probe
/// arguments, and version-parsing strategy — none of that is exposed here, and none of it is
/// ever project-owned or user-configurable in this slice.
/// </summary>
public interface IToolDiscoveryAdapter
{
    Task<ToolDiscoveryResult> DiscoverAsync(Capability capability, CancellationToken cancellationToken);
}
