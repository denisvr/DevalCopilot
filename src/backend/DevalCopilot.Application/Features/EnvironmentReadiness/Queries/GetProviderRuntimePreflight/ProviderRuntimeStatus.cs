namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetProviderRuntimePreflight;

/// <summary>
/// Installation/version-observation status only. <see cref="Available"/> never asserts
/// authentication, configuration, or eligibility to invoke a provider.
/// </summary>
public enum ProviderRuntimeStatus
{
    Checking = 0,
    Available = 1,
    Unavailable = 2,
    NeedsAttention = 3,
    Degraded = 4,
}
