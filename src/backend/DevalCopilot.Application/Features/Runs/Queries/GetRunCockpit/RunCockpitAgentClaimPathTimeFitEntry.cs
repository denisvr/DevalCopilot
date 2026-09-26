namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <summary>
/// The closed, honest fit outcome for one <see cref="AgentClaimPath"/>'s own configured
/// invocation timeout against the run's currently remaining reserved Agent invocation time (see
/// <see cref="RunCockpitAgentInvocationTimeBudgetSummary"/>). Advisory only — it is never a grant,
/// never "eligible", and never a substitute for the server's own authoritative check at claim time
/// (ADR-0013). A positive <see cref="Fits"/> result reflects only that this candidate's own
/// configured timeout is not already known to exceed the currently remaining reservation; every
/// other claim precondition (count budget, role-specific eligibility, workspace/checkpoint state,
/// authorization) is evaluated independently by the server.
/// </summary>
public enum AgentClaimPathTimeFit
{
    /// <summary>The claim path's configured timeout is less than or equal to the run's currently
    /// remaining reserved invocation time — "fits with zero slack" still counts as fitting.</summary>
    Fits,

    /// <summary>The claim path's configured timeout exceeds the run's currently remaining reserved
    /// invocation time.</summary>
    DoesNotFit,

    /// <summary>This run predates the invocation-time budget policy (ADR-0013) entirely — not
    /// itself a fit or no-fit answer, matching
    /// <see cref="RunCockpitAgentInvocationTimeBudgetSummary.IsLegacyUnknown"/>.</summary>
    LegacyUnknown,

    /// <summary>This run carries a real time-budget policy, but its prior Agent-attempt invocation-
    /// time evidence is missing or malformed, so no fit determination can be made — matches
    /// <see cref="RunCockpitAgentInvocationTimeBudgetSummary.EvidenceInvalid"/>.</summary>
    EvidenceInvalid,
}

/// <param name="ClaimPath">The claim path this fit result describes.</param>
/// <param name="Fit">The closed, honest fit outcome for this claim path.</param>
/// <remarks>
/// Carries only the claim path and its fit outcome — no fixed role/provider mapping. The six
/// cockpit action components each already know their own claim path statically (each component
/// IS a specific claim path), so they need only the fit result for that path, never a
/// server-repeated role/provider that duplicates <see cref="AgentClaimPathPolicy"/> without any
/// present consumer.
/// </remarks>
public sealed record RunCockpitAgentClaimPathTimeFitEntry(
    AgentClaimPath ClaimPath,
    AgentClaimPathTimeFit Fit);
