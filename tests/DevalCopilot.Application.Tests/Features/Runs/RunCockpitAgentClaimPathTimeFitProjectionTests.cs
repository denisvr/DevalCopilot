using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Coverage for <see cref="RunCockpitAgentClaimPathTimeFitProjection"/>: the additive, advisory
/// per-claim-path time-fit signal computed from the run-wide reserved-time budget the cockpit
/// query already computes (<see cref="RunCockpitAgentInvocationTimeBudgetSummary"/>) and the six
/// centrally configured timeouts (<see cref="AgentClaimPathPolicy"/>). Never re-derives the
/// reservation itself — every case here feeds a pre-built summary in, exactly mirroring what
/// <c>GetRunCockpitQueryHandler</c> already computed by the time it calls <c>Compute</c>.
/// </summary>
public sealed class RunCockpitAgentClaimPathTimeFitProjectionTests
{
    [Fact]
    public void Compute_always_returns_exactly_one_entry_per_claim_path()
    {
        var summary = RunCockpitAgentInvocationTimeBudgetSummary.Budgeted(TimeSpan.FromMinutes(120), TimeSpan.Zero);

        var entries = RunCockpitAgentClaimPathTimeFitProjection.Compute(summary);

        Assert.Equal(Enum.GetValues<AgentClaimPath>().Length, entries.Count);
        foreach (var claimPath in Enum.GetValues<AgentClaimPath>())
        {
            Assert.Single(entries, entry => entry.ClaimPath == claimPath);
        }
    }

    [Fact]
    public void A_claim_path_whose_configured_timeout_exactly_equals_the_remaining_time_fits()
    {
        // CodexPlanning is configured for exactly 10 minutes. "Fits with zero slack" still fits.
        var summary = RunCockpitAgentInvocationTimeBudgetSummary.Budgeted(
            maximum: TimeSpan.FromMinutes(120), reserved: TimeSpan.FromMinutes(110));

        var entries = RunCockpitAgentClaimPathTimeFitProjection.Compute(summary);

        var codexPlanning = Assert.Single(entries, entry => entry.ClaimPath == AgentClaimPath.CodexPlanning);
        Assert.Equal(AgentClaimPathTimeFit.Fits, codexPlanning.Fit);
    }

    [Fact]
    public void A_claim_path_whose_configured_timeout_exceeds_the_remaining_time_by_one_tick_does_not_fit()
    {
        var summary = RunCockpitAgentInvocationTimeBudgetSummary.Budgeted(
            maximum: TimeSpan.FromMinutes(120),
            reserved: TimeSpan.FromMinutes(110) + TimeSpan.FromTicks(1));

        var entries = RunCockpitAgentClaimPathTimeFitProjection.Compute(summary);

        var codexPlanning = Assert.Single(entries, entry => entry.ClaimPath == AgentClaimPath.CodexPlanning);
        Assert.Equal(AgentClaimPathTimeFit.DoesNotFit, codexPlanning.Fit);
    }

    [Fact]
    public void The_ten_minute_split_example_scenario_is_correct_for_every_claim_path()
    {
        // Exactly 10 minutes remaining: every 10-minute-configured claim path fits, every
        // 20-minute-configured one does not.
        var summary = RunCockpitAgentInvocationTimeBudgetSummary.Budgeted(
            maximum: TimeSpan.FromMinutes(10), reserved: TimeSpan.Zero);

        var entries = RunCockpitAgentClaimPathTimeFitProjection.Compute(summary);
        var byPath = entries.ToDictionary(entry => entry.ClaimPath);

        Assert.Equal(AgentClaimPathTimeFit.Fits, byPath[AgentClaimPath.CodexPlanning].Fit);
        Assert.Equal(AgentClaimPathTimeFit.Fits, byPath[AgentClaimPath.ClaudeCriticalReview].Fit);
        Assert.Equal(AgentClaimPathTimeFit.Fits, byPath[AgentClaimPath.ChallengeResolution].Fit);
        Assert.Equal(AgentClaimPathTimeFit.Fits, byPath[AgentClaimPath.CodeReview].Fit);
        Assert.Equal(AgentClaimPathTimeFit.DoesNotFit, byPath[AgentClaimPath.Implementation].Fit);
        Assert.Equal(AgentClaimPathTimeFit.DoesNotFit, byPath[AgentClaimPath.ReviewCorrection].Fit);
    }

    [Fact]
    public void A_legacy_run_with_no_time_policy_reports_LegacyUnknown_for_every_claim_path_never_Fits_or_DoesNotFit()
    {
        var entries = RunCockpitAgentClaimPathTimeFitProjection.Compute(RunCockpitAgentInvocationTimeBudgetSummary.LegacyUnknown());

        Assert.All(entries, entry => Assert.Equal(AgentClaimPathTimeFit.LegacyUnknown, entry.Fit));
    }

    [Fact]
    public void A_run_with_invalid_prior_evidence_reports_EvidenceInvalid_for_every_claim_path()
    {
        var summary = RunCockpitAgentInvocationTimeBudgetSummary.EvidenceInvalidFor(TimeSpan.FromMinutes(120));

        var entries = RunCockpitAgentClaimPathTimeFitProjection.Compute(summary);

        Assert.All(entries, entry => Assert.Equal(AgentClaimPathTimeFit.EvidenceInvalid, entry.Fit));
    }
}
