namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance;

/// <summary>
/// What <see cref="ICodexAccountAllowanceAdapter"/> reports back to the query handler. Provider-
/// neutral and already bounded to project-owned models — the adapter never leaks a raw JSON-RPC
/// payload, wire model, or provider exception across this port.
/// </summary>
public sealed record CodexAccountAllowanceObservation(
    bool IsObserved,
    DateTimeOffset? RetrievedAtUtc,
    IReadOnlyList<CodexAllowanceBucket> Buckets)
{
    public static readonly CodexAccountAllowanceObservation Unknown = new(false, null, []);
}
