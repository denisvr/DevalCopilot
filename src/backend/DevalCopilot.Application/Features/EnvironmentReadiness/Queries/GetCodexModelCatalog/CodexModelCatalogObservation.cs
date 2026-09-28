namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;

/// <summary>
/// What <see cref="ICodexModelCatalogAdapter"/> reports back to the query handler. Provider-
/// neutral and already bounded to project-owned models — the adapter never leaks a raw JSON-RPC
/// payload, wire model, or provider exception across this port.
/// </summary>
public sealed record CodexModelCatalogObservation(
    bool IsObserved,
    DateTimeOffset? RetrievedAtUtc,
    IReadOnlyList<CodexModelCatalogEntry> Models)
{
    public static readonly CodexModelCatalogObservation Unknown = new(false, null, []);
}
