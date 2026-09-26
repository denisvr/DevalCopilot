namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// Computes the closed, per-claim-path advisory time-fit projection from the run-wide Agent
/// invocation-time budget the cockpit query already computed (<see cref="RunCockpitAgentInvocationTimeBudgetSummary"/>)
/// and the six centrally configured timeouts (<see cref="AgentClaimPathPolicy"/>). Deliberately a
/// pure, dependency-free computation over already-loaded data — it issues no additional query and
/// materializes no additional entity, so adding it to <c>GetRunCockpitQueryHandler</c>'s existing
/// single bounded projection pass costs nothing beyond this in-memory switch.
/// </summary>
public static class RunCockpitAgentClaimPathTimeFitProjection
{
    private static readonly AgentClaimPath[] ClaimPaths =
    [
        AgentClaimPath.CodexPlanning,
        AgentClaimPath.ClaudeCriticalReview,
        AgentClaimPath.ChallengeResolution,
        AgentClaimPath.Implementation,
        AgentClaimPath.CodeReview,
        AgentClaimPath.ReviewCorrection,
    ];

    public static IReadOnlyList<RunCockpitAgentClaimPathTimeFitEntry> Compute(
        RunCockpitAgentInvocationTimeBudgetSummary agentInvocationTimeBudget)
    {
        ArgumentNullException.ThrowIfNull(agentInvocationTimeBudget);

        var entries = new RunCockpitAgentClaimPathTimeFitEntry[ClaimPaths.Length];
        for (var index = 0; index < ClaimPaths.Length; index++)
        {
            var claimPath = ClaimPaths[index];
            var fit = ComputeFit(agentInvocationTimeBudget, claimPath);
            entries[index] = new RunCockpitAgentClaimPathTimeFitEntry(claimPath, fit);
        }

        return entries;
    }

    private static AgentClaimPathTimeFit ComputeFit(
        RunCockpitAgentInvocationTimeBudgetSummary agentInvocationTimeBudget, AgentClaimPath claimPath)
    {
        if (agentInvocationTimeBudget.IsLegacyUnknown)
        {
            return AgentClaimPathTimeFit.LegacyUnknown;
        }

        if (agentInvocationTimeBudget.EvidenceInvalid || agentInvocationTimeBudget.Remaining is not { } remaining)
        {
            return AgentClaimPathTimeFit.EvidenceInvalid;
        }

        var candidateTimeout = AgentClaimPathPolicy.GetInvocationTimeout(claimPath);

        // Exact TimeSpan/tick-level comparison against the same Remaining value the run-wide
        // budget already computed — never the API's own independently millisecond-truncated
        // fields. "Fits with zero slack" (candidateTimeout == remaining exactly) still fits.
        return candidateTimeout <= remaining ? AgentClaimPathTimeFit.Fits : AgentClaimPathTimeFit.DoesNotFit;
    }
}
