using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The one fixed JSON Schema this slice ever asks Codex to constrain its challenge-resolution
/// final response to — the project-owned Decision-set-plus-revised-Proposal shape, not a provider
/// transcript. A single closed shape: a bounded <c>summary</c>, one <c>decisions</c> entry per
/// input Challenge (identified by <c>challengeMessageId</c>, with a closed <c>resolution</c>
/// enum plus <c>rationale</c>/<c>resultingPlanChanges</c>/<c>nextAction</c>), and exactly one
/// <c>revisedProposal</c> carrying the existing Proposal fields. Shared between the context
/// manifest (so the provider sees the same shape it will be validated against) and the
/// Infrastructure Codex adapter (which serializes this same schema to the value passed via
/// <c>--output-schema</c>). The per-field bounds mirror <c>CollaborationMessageContentPolicy</c>
/// exactly, so a schema-conformant response is never rejected by Domain validation for exceeding
/// a bound the provider was never told about.
/// </summary>
public static class ChallengeResolutionOutputSchema
{
    public const string AcceptedResolution = "accepted";
    public const string PartiallyAcceptedResolution = "partiallyAccepted";
    public const string RejectedResolution = "rejected";

    /// <summary>Mirrors <c>ClaudeCriticalReviewOutputSchema.MinimumChallenges</c> — a resolution
    /// attempt is only ever claimed for a Challenged review, which always raised at least
    /// one.</summary>
    public const int MinimumDecisions = 1;

    /// <summary>Mirrors the maximum Challenge cardinality a Claude critical-review attempt may
    /// ever produce (<c>ClaudeCriticalReviewOutputSchema.MaximumChallenges</c>) — a resolution
    /// attempt never needs to resolve more Challenges than a review could ever raise.</summary>
    public const int MaximumDecisions = 5;

    /// <summary>Mirrors the private bound in <c>CollaborationMessageContentPolicy</c> — kept as a
    /// literal here rather than exposing that Domain internal, since this is the one place outside
    /// Domain that legitimately needs the number for schema generation.</summary>
    private const int MaximumFieldLength = 900;

    // "summary" mirrors the same field every other message-producing item in this protocol
    // carries (e.g. each Challenge item in ClaudeCriticalReviewOutputSchema): the bounded,
    // top-level CollaborationMessage.Summary every recorded message requires, distinct from the
    // structured-content fields below it.
    private static readonly IReadOnlyList<string> DecisionFields =
        ["challengeMessageId", "summary", "resolution", "rationale", "resultingPlanChanges", "nextAction"];

    private static readonly IReadOnlyList<string> RevisedProposalFields =
        ["summary", "scope", "implementationSteps", "risks", "verificationPlan", "escalationPoints"];

    public static object BuildSchemaDocument() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "summary", "decisions", "revisedProposal" },
        properties = new Dictionary<string, object>
        {
            ["summary"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
            ["decisions"] = new
            {
                type = "array",
                minItems = MinimumDecisions,
                maxItems = MaximumDecisions,
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = DecisionFields,
                    properties = new Dictionary<string, object>
                    {
                        ["challengeMessageId"] = new { type = "string", format = "uuid" },
                        ["summary"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
                        ["resolution"] = new { type = "string", @enum = new[] { AcceptedResolution, PartiallyAcceptedResolution, RejectedResolution } },
                        ["rationale"] = StringSchema(1, MaximumFieldLength),
                        ["resultingPlanChanges"] = StringSchema(1, MaximumFieldLength),
                        ["nextAction"] = StringSchema(1, MaximumFieldLength),
                    },
                },
            },
            ["revisedProposal"] = new
            {
                type = "object",
                additionalProperties = false,
                required = RevisedProposalFields,
                properties = new Dictionary<string, object>
                {
                    ["summary"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
                    ["scope"] = StringSchema(1, MaximumFieldLength),
                    ["implementationSteps"] = StringSchema(1, MaximumFieldLength),
                    ["risks"] = StringSchema(1, MaximumFieldLength),
                    ["verificationPlan"] = StringSchema(1, MaximumFieldLength),
                    ["escalationPoints"] = StringSchema(1, MaximumFieldLength),
                },
            },
        },
    };

    private static object StringSchema(int minLength, int maxLength) => new
    {
        type = "string",
        minLength,
        maxLength,
    };
}
