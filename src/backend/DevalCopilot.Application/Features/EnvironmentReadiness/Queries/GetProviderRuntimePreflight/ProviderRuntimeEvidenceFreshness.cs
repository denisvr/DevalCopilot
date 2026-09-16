namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetProviderRuntimePreflight;

public enum ProviderRuntimeEvidenceFreshness
{
    NotObserved = 0,
    Fresh = 1,
    Stale = 2,
}
