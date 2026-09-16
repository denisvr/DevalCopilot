namespace DevalCopilot.Api.Features.EnvironmentReadiness.GetProviderRuntimePreflight;

/// <summary>
/// Public preflight evidence for a local provider runtime. Available proves only an observed
/// executable version; authentication and invocation capability remain explicitly unobserved.
/// </summary>
public sealed record ProviderRuntimePreflightResponse(
    string Provider,
    string Status,
    string? ObservedVersion,
    DateTimeOffset? EvidenceObservedAtUtc,
    string EvidenceFreshness,
    string ReasonCode,
    string ReasonMessage,
    string Authentication,
    string ModelCatalog,
    string ReasoningEffort,
    string PermissionMode,
    string ContextUsage,
    string Compaction,
    string Sessions,
    string AccountUsage);
