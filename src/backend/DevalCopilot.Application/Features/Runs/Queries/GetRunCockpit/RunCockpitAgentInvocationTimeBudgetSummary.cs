namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// A bounded, truthful projection of the run-wide Agent invocation-TIME budget (see the companion
/// ADR to ADR-0012) — deliberately distinct from <see cref="GetRunCockpitQueryResult.MaximumAgentAttempts"/>'s
/// own count-budget projection, never combined into one generic "exhausted" flag: some role
/// timeouts might still fit a run's remaining reservation while others would not, so this exposes
/// <see cref="Remaining"/> itself rather than a single boolean, letting a caller determine
/// per-candidate fit on its own. <see cref="Reserved"/> is never asserted to be elapsed wall-clock
/// time — it is the sum of what claimed Agent attempts have permanently reserved, independent of
/// whether they ever actually ran that long.
/// </summary>
/// <param name="Maximum">This run's own fixed reservation ceiling, or <see langword="null"/> when
/// <see cref="IsLegacyUnknown"/> is <see langword="true"/>.</param>
/// <param name="Reserved">The total time this run's claimed Agent attempts have permanently
/// reserved, or <see langword="null"/> when <see cref="IsLegacyUnknown"/> or
/// <see cref="EvidenceInvalid"/> is <see langword="true"/>.</param>
/// <param name="Remaining"><see cref="Maximum"/> minus <see cref="Reserved"/>, or
/// <see langword="null"/> under the same conditions as <see cref="Reserved"/>.</param>
/// <param name="IsLegacyUnknown"><see langword="true"/> only for a historical Run that predates
/// this decision — this Run truthfully has no time-budget policy at all, never a fabricated one.
/// Distinct from "0 minutes remaining", which would falsely suggest the Run is bound by, and has
/// exhausted, a real ceiling.</param>
/// <param name="EvidenceInvalid"><see langword="true"/> only when this Run does carry a real
/// policy but its prior Agent attempts' own invocation-time evidence is missing or malformed, so
/// the reservation cannot be safely computed — fails closed rather than understating usage.</param>
public sealed record RunCockpitAgentInvocationTimeBudgetSummary(
    TimeSpan? Maximum,
    TimeSpan? Reserved,
    TimeSpan? Remaining,
    bool IsLegacyUnknown,
    bool EvidenceInvalid)
{
    public static RunCockpitAgentInvocationTimeBudgetSummary LegacyUnknown() =>
        new(null, null, null, IsLegacyUnknown: true, EvidenceInvalid: false);

    public static RunCockpitAgentInvocationTimeBudgetSummary EvidenceInvalidFor(TimeSpan maximum) =>
        new(maximum, null, null, IsLegacyUnknown: false, EvidenceInvalid: true);

    public static RunCockpitAgentInvocationTimeBudgetSummary Budgeted(TimeSpan maximum, TimeSpan reserved) =>
        new(maximum, reserved, maximum - reserved, IsLegacyUnknown: false, EvidenceInvalid: false);
}
