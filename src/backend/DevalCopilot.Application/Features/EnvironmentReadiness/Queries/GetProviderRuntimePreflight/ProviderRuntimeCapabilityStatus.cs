namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetProviderRuntimePreflight;

/// <summary>
/// A deliberately narrow preflight declaration. Unknown means the host has not independently
/// observed the provider fact. Unsupported is deliberately absent until a reviewed provider
/// adapter observes that a provider does not support a particular capability.
/// </summary>
public enum ProviderRuntimeCapabilityStatus
{
    Unknown = 0,
}
