namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetProviderRuntimePreflight;

/// <summary>
/// Safe, host-scoped preflight for one provider runtime. It intentionally omits executable
/// paths, probe output, configuration, credentials, and every unobserved provider detail.
/// </summary>
public sealed record ProviderRuntimePreflightQueryResult(
    ProviderRuntime Provider,
    ProviderRuntimeStatus Status,
    string? ObservedVersion,
    DateTimeOffset? EvidenceObservedAtUtc,
    ProviderRuntimeEvidenceFreshness EvidenceFreshness,
    string ReasonCode,
    string ReasonMessage,
    ProviderRuntimeCapabilityStatus Authentication,
    ProviderRuntimeCapabilityStatus ModelCatalog,
    ProviderRuntimeCapabilityStatus ReasoningEffort,
    ProviderRuntimeCapabilityStatus PermissionMode,
    ProviderRuntimeCapabilityStatus ContextUsage,
    ProviderRuntimeCapabilityStatus Compaction,
    ProviderRuntimeCapabilityStatus Sessions,
    ProviderRuntimeCapabilityStatus AccountUsage);
