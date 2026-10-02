using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;

/// <summary>
/// The one fixed JSON Schema this slice ever asks Codex to constrain its verification-diagnosis final response to (ADR-0018):
/// a strict discriminated Findings-or-Escalation shape, never an approval. <c>findings</c> carries one to ten findings
/// (each with the same closed severity/category and bounds an ordinary review finding has) and <c>escalation</c> is null;
/// <c>escalation</c> carries the five bounded fields of an Escalation message and <c>findings</c> is empty.
/// <see cref="VerificationDiagnosisResponseParser"/> is the sole place that enforces the two shapes are never mixed. The
/// schema is shared between the context manifest (so the provider sees the shape it is validated against) and the
/// Infrastructure Codex adapter (which passes the same document through <c>--output-schema</c>).
/// </summary>
public static class VerificationDiagnosisOutputSchema
{
    public const string FindingsOutcome = "findings";
    public const string EscalationOutcome = "escalation";

    public const int MinimumFindings = VerificationDiagnosisPolicy.MinimumFindings;
    public const int MaximumFindings = VerificationDiagnosisPolicy.MaximumFindings;

    /// <summary>Mirrors the private bound in <c>CollaborationMessageContentPolicy</c>.</summary>
    private const int MaximumFieldLength = 900;

    private const int MaximumRelativePathLength = 512;

    public static readonly IReadOnlyList<string> EscalationFields =
        ["unresolvedDecision", "options", "consequences", "evidence", "recommendedChoice"];

    private static readonly IReadOnlyList<string> FindingFields =
        ["severity", "category", "summary", "evidence", "requiredChange", "affectedRelativePath"];

    public static object BuildSchemaDocument() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "outcome", "summary", "findings", "escalation" },
        properties = new Dictionary<string, object>
        {
            ["outcome"] = new { type = "string", @enum = new[] { FindingsOutcome, EscalationOutcome } },
            ["summary"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
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
                        ["severity"] = new { type = "string", @enum = ImplementationReviewOutputSchema.Severities },
                        ["category"] = new { type = "string", @enum = ImplementationReviewOutputSchema.Categories },
                        ["summary"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
                        ["evidence"] = StringSchema(1, MaximumFieldLength),
                        ["requiredChange"] = StringSchema(1, MaximumFieldLength),
                        ["affectedRelativePath"] = NullableStringSchema(1, MaximumRelativePathLength),
                    },
                },
            },
            ["escalation"] = new
            {
                type = new[] { "object", "null" },
                additionalProperties = false,
                required = EscalationFields,
                properties = EscalationFields.ToDictionary(field => field, _ => (object)StringSchema(1, MaximumFieldLength)),
            },
        },
    };

    private static object StringSchema(int minLength, int maxLength) => new { type = "string", minLength, maxLength };

    private static object NullableStringSchema(int minLength, int maxLength) => new
    {
        type = new[] { "string", "null" },
        minLength,
        maxLength,
    };
}
