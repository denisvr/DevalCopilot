using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The one fixed JSON Schema this slice ever asks Claude to constrain its critical-review final
/// response to. It is a transport schema: a single flat, closed object with <c>decision</c> (<c>accept</c>
/// or <c>challenge</c>) and <c>summary</c> required and <c>rationale</c> and <c>challenges</c> optional,
/// because the Claude tool input schema rejects a root <c>oneOf</c>, <c>anyOf</c> or <c>allOf</c>. The
/// exclusivity of the two optional fields is stated in the property descriptions only: an Acceptance
/// carries <c>rationale</c> and no <c>challenges</c>; a Challenge set carries one to five
/// <c>challenges</c> (each with its own <c>summary</c>, <c>disputedItem</c>, <c>materialImpact</c>,
/// <c>reasoning</c>, <c>alternativeOrQuestion</c>) and no <c>rationale</c>. The exact union, every
/// field set and every content rule stay enforced by <c>ClaudeCriticalReviewResponseParser</c> and
/// <c>CollaborationMessageContentPolicy</c>; this schema cannot establish that a live request passes.
/// Shared between the context manifests (ordinary and format-repair, so the provider sees the same
/// shape it will be validated against) and the Infrastructure Claude adapter (which serializes this
/// same schema to the value passed via <c>--json-schema</c>). The Claude tool input schema also does not
/// accept <c>minLength</c>, <c>maxLength</c> or <c>maxItems</c>, so none appears at any depth: the text
/// bounds and the five-challenge maximum are stated in the descriptions (mirroring
/// <c>CollaborationMessageContentPolicy</c>, so the provider is told every bound) and are enforced only
/// by the parser and the Domain policy. <c>minItems</c> stays.
/// </summary>
public static class ClaudeCriticalReviewOutputSchema
{
    public const string AcceptDecision = "accept";
    public const string ChallengeDecision = "challenge";
    public const int MinimumChallenges = 1;
    public const int MaximumChallenges = 5;

    /// <summary>Mirrors the private bound in <c>CollaborationMessageContentPolicy</c> — kept as a
    /// literal here rather than exposing that Domain internal, since this is the one place outside
    /// Domain that legitimately needs the number for schema generation.</summary>
    private const int MaximumFieldLength = 900;

    public static object BuildSchemaDocument() => new
    {
        type = "object",
        description = "Exactly one review decision. For decision accept, include rationale and omit challenges. "
            + "For decision challenge, include challenges and omit rationale.",
        additionalProperties = false,
        required = new[] { "decision", "summary" },
        properties = new Dictionary<string, object>
        {
            ["decision"] = new
            {
                type = "string",
                @enum = new[] { AcceptDecision, ChallengeDecision },
                description = "accept approves the reviewed proposal; challenge disputes it.",
            },
            ["summary"] = StringSchema(
                $"Plain-text summary of the decision, 1 to {CollaborationMessageContentPolicy.MaximumSummaryLength} characters."),
            ["rationale"] = StringSchema(
                $"Plain-text reasoning, 1 to {MaximumFieldLength} characters. Required when decision is accept; "
                    + "must be omitted when decision is challenge."),
            ["challenges"] = new
            {
                type = "array",
                minItems = MinimumChallenges,
                description = $"{MinimumChallenges} to {MaximumChallenges} challenges. Required when decision is challenge; "
                    + "must be omitted when decision is accept.",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = ChallengeItemFields,
                    properties = ChallengeItemFields.ToDictionary(
                        field => field,
                        field => (object)StringSchema(
                            $"Plain text, 1 to {(field == "summary" ? CollaborationMessageContentPolicy.MaximumSummaryLength : MaximumFieldLength)} characters.")),
                },
            },
        },
    };

    private static readonly IReadOnlyList<string> ChallengeItemFields =
        ["summary", "disputedItem", "materialImpact", "reasoning", "alternativeOrQuestion"];

    private static object StringSchema(string description) => new
    {
        type = "string",
        description,
    };
}
