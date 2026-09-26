using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

namespace DevalCopilot.Api.Features.Runs.GetRunCockpit;

/// <summary>
/// The API projection of one <c>AgentClaimPath</c>'s advisory time-fit result. Every status
/// crosses the wire as a plain string (this repo's established convention — zero generated
/// TypeScript <c>enum</c>), never a numeric or generated enum value. Advisory only: a
/// <c>"Fits"</c> value is never a grant or a promise the server will accept the claim. Carries
/// only the claim path and its fit outcome — no role/provider mapping crosses the wire here; the
/// six cockpit action components already know their own claim path statically.
/// </summary>
public sealed record AgentClaimPathTimeFitResponse(string ClaimPath, string Fit)
{
    public static AgentClaimPathTimeFitResponse FromDomain(RunCockpitAgentClaimPathTimeFitEntry entry) =>
        new(entry.ClaimPath.ToString(), entry.Fit.ToString());
}
