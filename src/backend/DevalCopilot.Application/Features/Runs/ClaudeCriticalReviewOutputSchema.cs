using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The one fixed JSON Schema this slice ever asks Claude to constrain its critical-review final
/// response to — the project-owned Acceptance/Challenge union, not a provider-native transcript.
/// A single closed shape, discriminated by <c>decision</c>: exactly one of an Acceptance
/// (<c>summary</c>, <c>rationale</c>) or a Challenge set (<c>summary</c>, one to five
/// <c>challenges</c>, each with its own <c>summary</c>, <c>disputedItem</c>,
/// <c>materialImpact</c>, <c>reasoning</c>, <c>alternativeOrQuestion</c>) — never both, never
/// neither. Shared between the context manifest (so the provider sees the same shape it will be
/// validated against) and the Infrastructure Claude adapter (which serializes this same schema to
/// the value passed via <c>--json-schema</c>). The bounds mirror
/// <c>CollaborationMessageContentPolicy</c> exactly, so a schema-conformant response is never
/// rejected by Domain validation for exceeding a bound the provider was never told about.
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
        oneOf = new object[]
        {
            new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "decision", "summary", "rationale" },
                properties = new Dictionary<string, object>
                {
                    ["decision"] = new { type = "string", @const = AcceptDecision },
                    ["summary"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
                    ["rationale"] = StringSchema(1, MaximumFieldLength),
                },
            },
            new
            {
                type = "object",
                additionalProperties = false,
                required = new[] { "decision", "summary", "challenges" },
                properties = new Dictionary<string, object>
                {
                    ["decision"] = new { type = "string", @const = ChallengeDecision },
                    ["summary"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
                    ["challenges"] = new
                    {
                        type = "array",
                        minItems = MinimumChallenges,
                        maxItems = MaximumChallenges,
                        items = new
                        {
                            type = "object",
                            additionalProperties = false,
                            required = ChallengeItemFields,
                            properties = ChallengeItemFields.ToDictionary(
                                field => field,
                                field => (object)StringSchema(
                                    1, field == "summary" ? CollaborationMessageContentPolicy.MaximumSummaryLength : MaximumFieldLength)),
                        },
                    },
                },
            },
        },
    };

    private static readonly IReadOnlyList<string> ChallengeItemFields =
        ["summary", "disputedItem", "materialImpact", "reasoning", "alternativeOrQuestion"];

    private static object StringSchema(int minLength, int maxLength) => new
    {
        type = "string",
        minLength,
        maxLength,
    };
}
