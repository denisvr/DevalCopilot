using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The one fixed JSON Schema this slice ever asks Codex to constrain its final response to —
/// the project-owned Proposal shape, not a provider-native transcript. Shared between the
/// context manifest (so the provider sees the same shape it will be validated against) and the
/// Infrastructure Codex adapter (which serializes this same schema to the file passed via
/// <c>--output-schema</c>). The bounds mirror <c>CollaborationMessageContentPolicy</c> exactly,
/// so a schema-conformant response is never rejected by Domain validation for exceeding a bound
/// the provider was never told about.
/// </summary>
public static class CodexProposalOutputSchema
{
    /// <summary>The exact six top-level fields a valid Proposal final response must contain:
    /// the bounded top-level summary plus the five Domain-required structured-content fields, in
    /// a fixed order.</summary>
    public static readonly IReadOnlyList<string> RequiredFields =
    [
        "summary", "scope", "implementationSteps", "risks", "verificationPlan", "escalationPoints",
    ];

    public static object BuildSchemaDocument() => new
    {
        type = "object",
        additionalProperties = false,
        required = RequiredFields,
        properties = new Dictionary<string, object>
        {
            ["summary"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
            ["scope"] = StringSchema(1, MaximumFieldLength),
            ["implementationSteps"] = StringSchema(1, MaximumFieldLength),
            ["risks"] = StringSchema(1, MaximumFieldLength),
            ["verificationPlan"] = StringSchema(1, MaximumFieldLength),
            ["escalationPoints"] = StringSchema(1, MaximumFieldLength),
        },
    };

    /// <summary>Mirrors the private bound in <c>CollaborationMessageContentPolicy</c> — kept as
    /// a literal here rather than exposing that Domain internal, since this is the one place
    /// outside Domain that legitimately needs the number for schema generation.</summary>
    private const int MaximumFieldLength = 900;

    private static object StringSchema(int minLength, int maxLength) => new
    {
        type = "string",
        minLength,
        maxLength,
    };
}
