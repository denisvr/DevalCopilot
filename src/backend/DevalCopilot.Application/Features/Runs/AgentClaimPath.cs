namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The closed set of currently supported Agent-claiming operations. Distinct from
/// <see cref="Domain.Features.Runs.AgentRole"/>: a role (e.g.
/// <see cref="Domain.Features.Runs.AgentRole.Implementer"/>) can be occupied by more than one
/// claim path (<see cref="Implementation"/> and <see cref="ReviewCorrection"/>), and per ADR-0009
/// a role is never assumed to carry one fixed execution configuration for every path that claims
/// it. This is execution/claim-path configuration owned by the Application layer's six
/// Agent-claiming command handlers — not a Domain invariant — so it lives alongside them here,
/// not under <c>DevalCopilot.Domain</c>. Appended, never renumbered, as a slice adds a real,
/// proven claim path.
/// </summary>
public enum AgentClaimPath
{
    CodexPlanning = 0,
    ClaudeCriticalReview = 1,
    ChallengeResolution = 2,
    Implementation = 3,
    CodeReview = 4,
    ReviewCorrection = 5,
}
