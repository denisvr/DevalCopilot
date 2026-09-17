using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;

/// <summary>
/// The one unambiguous mapping from shared host evidence to the accepted UI vocabulary. Used
/// wherever a project needs to present capability readiness, so every caller sees the exact
/// same rule:
///
/// <list type="bullet">
/// <item>required + not found → Unavailable</item>
/// <item>optional (Docker) + not found → Degraded</item>
/// <item>timeout / inaccessible / interrupted-by-restart / ambiguous launch target → Needs
/// attention</item>
/// <item>unparseable version → Degraded</item>
/// <item>successful parsed probe → Ready</item>
/// <item>never probed → no display status yet (loading), not a fifth state</item>
/// </list>
///
/// Staleness is computed independently of all of the above and never changes which branch
/// fires — it only adds a qualifier on top of whatever status the evidence already implies.
/// </summary>
public static class HostCapabilityReadinessProjector
{
    /// <summary>
    /// Grace beyond a capability's own due time before its evidence is presented as stale.
    /// Comfortably larger than the supervisor's poll interval so a capability that is refreshed
    /// promptly right after becoming due is never flashed as stale in the meantime.
    /// </summary>
    private static readonly TimeSpan StalenessGrace = TimeSpan.FromMinutes(2);

    public static IReadOnlyList<CapabilityReadinessQueryResult> Project(
        IReadOnlyList<HostCapabilitySnapshot> snapshots, DateTimeOffset nowUtc)
    {
        var snapshotsByCapability = snapshots.ToDictionary(snapshot => snapshot.Capability);

        return CapabilityCatalog.All
            .Select(capability => ProjectOne(capability, snapshotsByCapability, nowUtc))
            .ToArray();
    }

    private static CapabilityReadinessQueryResult ProjectOne(
        Capability capability, IReadOnlyDictionary<Capability, HostCapabilitySnapshot> snapshotsByCapability, DateTimeOffset nowUtc)
    {
        var isRequired = CapabilityCatalog.IsRequiredByDefault(capability);

        // No row yet — seeding has not run for this capability. Presented identically to a
        // freshly seeded NeverProbed row: a loading state, never a fifth persisted status.
        if (!snapshotsByCapability.TryGetValue(capability, out var snapshot))
        {
            return new CapabilityReadinessQueryResult(
                capability, isRequired, DisplayStatus: null, CapabilityProbeReason.NeverProbed,
                ResolvedExecutablePath: null, Version: null, LastCheckedUtc: null, IsStale: false);
        }

        var displayStatus = MapDisplayStatus(snapshot.ReasonCode, isRequired);
        var isStale = nowUtc > snapshot.NextProbeDueAtUtc + StalenessGrace;

        return new CapabilityReadinessQueryResult(
            capability,
            isRequired,
            displayStatus,
            snapshot.ReasonCode,
            snapshot.ResolvedExecutablePath,
            snapshot.ObservedVersion,
            snapshot.EvidenceObservedAtUtc,
            isStale);
    }

    private static CapabilityDisplayStatus? MapDisplayStatus(CapabilityProbeReason reason, bool isRequired) => reason switch
    {
        CapabilityProbeReason.NeverProbed => null,
        CapabilityProbeReason.None => CapabilityDisplayStatus.Ready,
        CapabilityProbeReason.ExecutableNotFound => isRequired ? CapabilityDisplayStatus.Unavailable : CapabilityDisplayStatus.Degraded,
        CapabilityProbeReason.ExecutableInaccessible => CapabilityDisplayStatus.NeedsAttention,
        CapabilityProbeReason.ProbeTimedOut => CapabilityDisplayStatus.NeedsAttention,
        CapabilityProbeReason.VersionProbeUnparseable => CapabilityDisplayStatus.Degraded,
        CapabilityProbeReason.ProbeInterruptedByRestart => CapabilityDisplayStatus.NeedsAttention,
        CapabilityProbeReason.LaunchTargetAmbiguous => CapabilityDisplayStatus.NeedsAttention,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
