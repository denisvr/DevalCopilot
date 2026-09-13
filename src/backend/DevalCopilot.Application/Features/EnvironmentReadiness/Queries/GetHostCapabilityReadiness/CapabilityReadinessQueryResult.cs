using DevalCopilot.Domain.Features.EnvironmentReadiness;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;

/// <summary>
/// One capability's readiness as seen by a consuming project: the shared host evidence,
/// projected through that project's required/optional classification.
/// </summary>
/// <param name="DisplayStatus">Null only when <paramref name="ReasonCode"/> is
/// <see cref="CapabilityProbeReason.NeverProbed"/> — a loading/bootstrap presentation, never a
/// fifth persisted status.</param>
/// <param name="IsStale">An independent qualifier layered on top of
/// <paramref name="DisplayStatus"/> — never a fifth status of its own.</param>
public sealed record CapabilityReadinessQueryResult(
    Capability Capability,
    bool IsRequired,
    CapabilityDisplayStatus? DisplayStatus,
    CapabilityProbeReason ReasonCode,
    string? ResolvedExecutablePath,
    string? Version,
    DateTimeOffset? LastCheckedUtc,
    bool IsStale);
