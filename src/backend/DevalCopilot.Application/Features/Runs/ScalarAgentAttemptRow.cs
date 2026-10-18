namespace DevalCopilot.Application.Features.Runs;

/// <summary>The scalar facts of one attempt that the unchanged identity rule and a minimal header need, selected without materializing an
/// attempt or a stored enum: status, role, provider, contract and permission profile use 0 for a null column and 99 for a stored value
/// not known. Every assignment-validation string is read only within its byte guard and carries a flag, so a rejected oversized storage
/// is never mistaken for a legitimate null; the max-turn storage keeps its exact storage-class form.</summary>
internal sealed record ScalarAgentAttemptRow(
    Guid Id,
    int AttemptNumber,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    bool IsAgent,
    int StatusCode,
    int RoleCode,
    int ProviderCode,
    int ContractCode,
    int ProfileCode,
    string? RequestedModel,
    bool RequestedModelOversized,
    string? ObservedModel,
    bool ObservedModelOversized,
    string? RequestedEffort,
    bool RequestedEffortOversized,
    string? ObservedEffort,
    bool ObservedEffortOversized,
    string? AdapterContractVersion,
    bool AdapterContractVersionOversized,
    string? StoredMaxTurns,
    bool StoredMaxTurnsOversized);
