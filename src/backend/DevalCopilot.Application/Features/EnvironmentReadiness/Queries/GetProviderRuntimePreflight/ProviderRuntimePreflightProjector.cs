using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetProviderRuntimePreflight;

/// <summary>
/// Converts the generic host-capability display projection into provider-runtime language. All
/// discovery truth remains owned by <see cref="HostCapabilityReadinessProjector"/>.
/// </summary>
public static class ProviderRuntimePreflightProjector
{
    public static IReadOnlyList<ProviderRuntimePreflightQueryResult> Project(
        IReadOnlyList<CapabilityReadinessQueryResult> capabilities)
    {
        var capabilitiesByKind = capabilities.ToDictionary(capability => capability.Capability);

        return [
            ProjectOne(ProviderRuntime.Codex, Capability.CodexCli, capabilitiesByKind),
            ProjectOne(ProviderRuntime.ClaudeCode, Capability.ClaudeCli, capabilitiesByKind),
        ];
    }

    private static ProviderRuntimePreflightQueryResult ProjectOne(
        ProviderRuntime provider,
        Capability capability,
        IReadOnlyDictionary<Capability, CapabilityReadinessQueryResult> capabilitiesByKind)
    {
        var readiness = capabilitiesByKind[capability];
        var freshness = readiness.LastCheckedUtc is null
            ? ProviderRuntimeEvidenceFreshness.NotObserved
            : readiness.IsStale
                ? ProviderRuntimeEvidenceFreshness.Stale
                : ProviderRuntimeEvidenceFreshness.Fresh;

        return new ProviderRuntimePreflightQueryResult(
            provider,
            MapStatus(readiness.DisplayStatus),
            readiness.Version,
            readiness.LastCheckedUtc,
            freshness,
            MapReasonCode(readiness.ReasonCode),
            MapReasonMessage(readiness.ReasonCode),
            ProviderRuntimeCapabilityStatus.Unknown,
            ProviderRuntimeCapabilityStatus.Unknown,
            ProviderRuntimeCapabilityStatus.Unknown,
            ProviderRuntimeCapabilityStatus.Unknown,
            ProviderRuntimeCapabilityStatus.Unknown,
            ProviderRuntimeCapabilityStatus.Unknown,
            ProviderRuntimeCapabilityStatus.Unknown,
            ProviderRuntimeCapabilityStatus.Unknown);
    }

    private static ProviderRuntimeStatus MapStatus(CapabilityDisplayStatus? status) => status switch
    {
        null => ProviderRuntimeStatus.Checking,
        CapabilityDisplayStatus.Ready => ProviderRuntimeStatus.Available,
        CapabilityDisplayStatus.Unavailable => ProviderRuntimeStatus.Unavailable,
        CapabilityDisplayStatus.NeedsAttention => ProviderRuntimeStatus.NeedsAttention,
        CapabilityDisplayStatus.Degraded => ProviderRuntimeStatus.Degraded,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static string MapReasonCode(CapabilityProbeReason reason) => reason switch
    {
        CapabilityProbeReason.NeverProbed => "provider_runtime.not_observed",
        CapabilityProbeReason.None => "provider_runtime.version_observed",
        CapabilityProbeReason.ExecutableNotFound => "provider_runtime.executable_not_found",
        CapabilityProbeReason.ExecutableInaccessible => "provider_runtime.version_probe_inaccessible",
        CapabilityProbeReason.ProbeTimedOut => "provider_runtime.version_probe_timed_out",
        CapabilityProbeReason.VersionProbeUnparseable => "provider_runtime.version_probe_unparseable",
        CapabilityProbeReason.ProbeInterruptedByRestart => "provider_runtime.version_probe_interrupted",
        CapabilityProbeReason.LaunchTargetAmbiguous => "provider_runtime.launch_target_ambiguous",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private static string MapReasonMessage(CapabilityProbeReason reason) => reason switch
    {
        CapabilityProbeReason.NeverProbed => "Runtime version has not been observed.",
        CapabilityProbeReason.None => "Runtime version was observed.",
        CapabilityProbeReason.ExecutableNotFound => "Runtime executable was not found.",
        CapabilityProbeReason.ExecutableInaccessible => "Runtime version could not be observed.",
        CapabilityProbeReason.ProbeTimedOut => "Runtime version probe timed out.",
        CapabilityProbeReason.VersionProbeUnparseable => "Runtime version was not recognized.",
        CapabilityProbeReason.ProbeInterruptedByRestart => "Runtime version probe was interrupted by host restart.",
        CapabilityProbeReason.LaunchTargetAmbiguous => "More than one runtime installation was found; resolution was stopped rather than guessed.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
