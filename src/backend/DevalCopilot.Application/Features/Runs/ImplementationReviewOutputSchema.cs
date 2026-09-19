using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The one fixed JSON Schema this slice ever asks Codex to constrain its implementation-review
/// final response to — a strict discriminated Approved-or-ChangesRequested shape, not a provider
/// transcript. Approved carries a bounded <c>rationale</c> and <c>residualRisks</c> and an empty
/// <c>findings</c> array; ChangesRequested carries one-to-ten <c>findings</c> and leaves
/// <c>rationale</c>/<c>residualRisks</c> null — <see cref="ImplementationReviewResponseParser"/>
/// is the sole place that enforces the two shapes are never mixed. Each finding's closed
/// <c>severity</c>/<c>category</c> and its <c>affectedRelativePath</c> mirror the bounds
/// <see cref="CollaborationMessageContentPolicy"/> applies once a finding is actually recorded —
/// a schema-conformant response is never rejected by Domain validation for exceeding a bound the
/// provider was never told about. <c>affectedRelativePath</c> is deliberately the one field this
/// schema allows that never reaches <see cref="CollaborationMessageContentPolicy.ReviewFindingFields"/> —
/// it stays only in the sealed final-response artifact, never duplicated into the durable ledger.
/// Shared between the context manifest (so the provider sees the same shape it will be validated
/// against) and the Infrastructure Codex adapter (which serializes this same schema to the value
/// passed via <c>--output-schema</c>).
/// </summary>
public static class ImplementationReviewOutputSchema
{
    public const string ApprovedOutcome = "approved";
    public const string ChangesRequestedOutcome = "changesRequested";

    public const int MinimumFindings = 1;
    public const int MaximumFindings = 10;

    /// <summary>Mirrors the private bound in <c>CollaborationMessageContentPolicy</c> — kept as a
    /// literal here rather than exposing that Domain internal, since this is the one place outside
    /// Domain that legitimately needs the number for schema generation.</summary>
    private const int MaximumFieldLength = 900;

    private const int MaximumRelativePathLength = 512;

    public static readonly IReadOnlyList<string> Severities = ["low", "medium", "high", "critical"];

    public static readonly IReadOnlyList<string> Categories =
        ["correctness", "security", "standards", "testCoverage", "design"];

    private static readonly IReadOnlyList<string> FindingFields =
        ["severity", "category", "summary", "evidence", "requiredChange", "affectedRelativePath"];

    public static object BuildSchemaDocument() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "outcome", "summary", "rationale", "residualRisks", "findings" },
        properties = new Dictionary<string, object>
        {
            ["outcome"] = new { type = "string", @enum = new[] { ApprovedOutcome, ChangesRequestedOutcome } },
            ["summary"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
            ["rationale"] = NullableStringSchema(1, MaximumFieldLength),
            ["residualRisks"] = NullableStringSchema(1, MaximumFieldLength),
            ["findings"] = new
            {
                type = "array",
                minItems = 0,
                maxItems = MaximumFindings,
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = FindingFields,
                    properties = new Dictionary<string, object>
                    {
                        ["severity"] = new { type = "string", @enum = Severities },
                        ["category"] = new { type = "string", @enum = Categories },
                        ["summary"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
                        ["evidence"] = StringSchema(1, MaximumFieldLength),
                        ["requiredChange"] = StringSchema(1, MaximumFieldLength),
                        ["affectedRelativePath"] = NullableStringSchema(1, MaximumRelativePathLength),
                    },
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

    private static object NullableStringSchema(int minLength, int maxLength) => new
    {
        type = new[] { "string", "null" },
        minLength,
        maxLength,
    };
}
