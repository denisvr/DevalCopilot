using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

namespace DevalCopilot.Api.Features.Runs.GetRunCockpit;

/// <summary>
/// The bounded, truthful API projection of the run-wide Agent invocation-TIME budget. Never a
/// single generic "exhausted" flag: <c>RemainingMilliseconds</c> lets the caller determine
/// per-candidate fit itself. <c>IsLegacyUnknown</c> is the truthful "this run predates the
/// time-budget policy" state — distinct from "0 milliseconds remaining", which would falsely imply
/// a real, exhausted ceiling.
/// </summary>
public sealed record AgentInvocationTimeBudgetResponse(
    long? MaximumMilliseconds,
    long? ReservedMilliseconds,
    long? RemainingMilliseconds,
    bool IsLegacyUnknown,
    bool EvidenceInvalid)
{
    public static AgentInvocationTimeBudgetResponse FromDomain(RunCockpitAgentInvocationTimeBudgetSummary summary) =>
        new(
            summary.Maximum is { } maximum ? (long)maximum.TotalMilliseconds : null,
            summary.Reserved is { } reserved ? (long)reserved.TotalMilliseconds : null,
            summary.Remaining is { } remaining ? (long)remaining.TotalMilliseconds : null,
            summary.IsLegacyUnknown,
            summary.EvidenceInvalid);
}
