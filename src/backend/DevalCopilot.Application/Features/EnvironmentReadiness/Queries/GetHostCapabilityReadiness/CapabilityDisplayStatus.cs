namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;

/// <summary>
/// The accepted product display vocabulary (see docs/product/user-experience.md). Exactly
/// these four — never a fifth persisted state. "Stale" is an independent qualifier alongside
/// this status, not a value of it; a capability with no evidence yet has no display status at
/// all (see <see cref="HostCapabilityReadinessProjector"/>).
/// </summary>
public enum CapabilityDisplayStatus
{
    Ready,
    Degraded,
    NeedsAttention,
    Unavailable,
}
