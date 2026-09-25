using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

namespace DevalCopilot.Api.Features.Runs.GetRunCockpit;

/// <summary>
/// The bounded, truthful API projection of the run-wide HOST-MEASURED Agent process-duration
/// evidence summary. Pure telemetry — never a budget, never combined with
/// <c>AgentBudgetExhausted</c> or <c>AgentInvocationTimeBudget</c>. <c>Status</c> is the closed
/// evidence-state classification (see <see cref="AgentProcessDurationEvidenceStatus"/>);
/// <c>TotalMeasuredMilliseconds</c> is populated only for <c>"Complete"</c>, so a real
/// zero-millisecond measurement is always distinguishable from "no measurement is available yet".
/// It is a <see cref="double"/>, not a truncated integer: a truncating integer-milliseconds
/// projection would silently collapse a genuinely positive sub-millisecond total (fewer than
/// 10,000 ticks) down to exactly zero, making it indistinguishable from a real zero-duration
/// measurement. The Domain-level sum this is projected from is exact (checked tick arithmetic
/// over real <see cref="TimeSpan"/> values); <see cref="TimeSpan.TotalMilliseconds"/> reliably
/// preserves the zero-versus-positive distinction from that exact sum, but a <see cref="double"/>
/// does not guarantee full tick-level precision is preserved at every magnitude — it is precise
/// enough for this field's zero/non-zero and coarse display purposes, not a promise of exact tick
/// fidelity in the wire format.
/// </summary>
public sealed record AgentProcessDurationSummaryResponse(
    string Status,
    double? TotalMeasuredMilliseconds,
    int DispatchedAttemptCount,
    int PendingAttemptCount,
    int ValidEvidenceCount,
    int MalformedEvidenceCount)
{
    public static AgentProcessDurationSummaryResponse FromDomain(RunCockpitAgentProcessDurationSummary summary) =>
        new(
            summary.Status.ToString(),
            summary.TotalMeasuredDuration?.TotalMilliseconds,
            summary.DispatchedAttemptCount,
            summary.PendingAttemptCount,
            summary.ValidEvidenceCount,
            summary.MalformedEvidenceCount);
}
