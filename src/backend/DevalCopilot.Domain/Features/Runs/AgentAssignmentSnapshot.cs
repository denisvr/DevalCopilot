namespace DevalCopilot.Domain.Features.Runs;

/// <summary>A bounded, Domain-owned projection of one Agent attempt's immutable assignment and
/// provider-observed facts. Requested values are fixed at claim time; observed values remain null
/// unless the provider's authoritative output supplied them.</summary>
public sealed record AgentAssignmentSnapshot(
    AgentProvider Provider,
    string? RequestedModel,
    string? ObservedModel,
    string? RequestedEffort,
    string? ObservedEffort,
    AgentPermissionProfile PermissionProfile,
    string? AdapterContractVersion);
