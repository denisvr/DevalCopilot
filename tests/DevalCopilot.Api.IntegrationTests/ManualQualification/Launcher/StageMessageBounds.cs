using DevalCopilot.Application.Features.Runs;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// How many produced messages a stage's accepted attempt may have, decided by its role and recorded outcome alone. One Planner attempt
/// records exactly one Proposal. One CriticalReviewer attempt records either exactly one Acceptance or a set of one to the schema's
/// maximum of Challenges. Any other role or outcome expects none. The same bound limits the message-evidence requests the reader
/// makes, and the number of messages a reading retains is separately capped so that an unbounded timeline cannot grow a summary.
/// </summary>
public static class StageMessageBounds
{
    /// <summary>The most messages a reading retains and the summary lists, one more than the largest valid set so that an excess is
    /// visible. The observed count is always carried separately and in full.</summary>
    public static readonly int MaximumListed = ClaudeCriticalReviewOutputSchema.MaximumChallenges + 1;

    public const string PlannerRole = "Planner";
    public const string ReviewerRole = "CriticalReviewer";

    /// <summary>The largest valid number of messages for the role and recorded outcome, and the number of evidence requests the reader
    /// may make for them.</summary>
    public static int Maximum(string? role, string? outcome) => (role, outcome) switch
    {
        (PlannerRole, "Proposed") => 1,
        (ReviewerRole, "Accepted") => 1,
        (ReviewerRole, "Challenged") => ClaudeCriticalReviewOutputSchema.MaximumChallenges,
        _ => 0,
    };
}
