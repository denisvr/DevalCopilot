namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The single source of truth for each currently supported <see cref="AgentClaimPath"/>'s own
/// configured Agent invocation timeout. Kept as one small, dependency-free Application-layer
/// helper — the same placement and shape as <see cref="AgentInvocationTimeBudget"/> — so every
/// one of the six Agent-claiming command handlers and the run-cockpit candidate-fit projection
/// read the identical six values, instead of each hardcoding its own copy that a future change
/// could let drift.
///
/// This is deliberately NOT placed on <c>AgentAttemptContract</c> (a request/response-shape
/// concept unrelated to execution timing) or treated as an intrinsic property of
/// <see cref="Domain.Features.Runs.AgentRole"/> (per ADR-0009, role, effect, and provider
/// assignment are kept separate; a role such as <see cref="Domain.Features.Runs.AgentRole.Implementer"/>
/// is already claimed by two distinct paths — <see cref="AgentClaimPath.Implementation"/> and
/// <see cref="AgentClaimPath.ReviewCorrection"/> — that happen to share a value today but are not
/// required to keep sharing it, and a future provider assignment may need its own execution
/// configuration independent of role). Nor is it a Domain invariant: it is execution/claim-path
/// configuration for the Application-layer command handlers that already own it, so it lives
/// under <c>DevalCopilot.Application</c> rather than <c>DevalCopilot.Domain</c>. This is not a
/// generic timeout-configuration system: it is exactly the smallest lookup covering the six
/// known, currently supported claim paths.
/// </summary>
public static class AgentClaimPathPolicy
{
    /// <summary>
    /// The exact configured Agent invocation timeout for a claim path — unchanged from each
    /// handler's own previously inline value; this is a centralization, not a value change.
    /// </summary>
    public static TimeSpan GetInvocationTimeout(AgentClaimPath claimPath) => claimPath switch
    {
        AgentClaimPath.CodexPlanning => TimeSpan.FromMinutes(10),
        AgentClaimPath.ClaudeCriticalReview => TimeSpan.FromMinutes(10),
        AgentClaimPath.ChallengeResolution => TimeSpan.FromMinutes(10),
        AgentClaimPath.Implementation => TimeSpan.FromMinutes(20),
        AgentClaimPath.CodeReview => TimeSpan.FromMinutes(10),
        AgentClaimPath.ReviewCorrection => TimeSpan.FromMinutes(20),
        _ => throw new ArgumentOutOfRangeException(nameof(claimPath), claimPath, "Unknown Agent claim path."),
    };
}
