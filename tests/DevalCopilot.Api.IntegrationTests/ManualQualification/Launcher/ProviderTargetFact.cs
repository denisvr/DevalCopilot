using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>One capability as the host's own readiness evidence records it: the discovery result, never a launcher guess.</summary>
public sealed record ProviderTargetFact(
    Capability Capability,
    CapabilityProbeReason Reason,
    CapabilityLaunchKind? LaunchKind,
    string? ExecutablePath,
    string? ScriptPath,
    string? Version);
