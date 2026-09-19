using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The one fixed JSON Schema this slice ever asks Claude to constrain its implementation final
/// response to. A single closed shape: a bounded <c>summary</c>, the exact set of repository-
/// relative paths it changed, bounded implementation notes, and bounded free-text fields for
/// anything unexpected it found and any risk or verification it wants to surface. Never a
/// transcript. <c>changedRelativePaths</c> is Claude's own self-report and is never trusted on its
/// own — <c>RecordImplementationResultCommandHandler</c> requires it to exactly match
/// independently observed Git evidence before any successful outcome is recorded. Shared between
/// the context manifest (so the provider sees the same shape it will be validated against) and
/// the Infrastructure Claude adapter (which serializes this same schema to the value passed via
/// <c>--json-schema</c>).
/// </summary>
public static class ImplementationReportOutputSchema
{
    /// <summary>Mirrors the private bound in <c>CollaborationMessageContentPolicy</c> — kept as a
    /// literal here rather than exposing that Domain internal, since this is the one place outside
    /// Domain that legitimately needs the number for schema generation. Internal, not private:
    /// <c>ImplementationResponseParser</c> and <c>ImplementationEvidenceValidation</c> both need
    /// the same bound for the free-text fields this schema does not otherwise close (the
    /// unexpected-discoveries/remaining-risks fields never reach <c>CollaborationMessageContentPolicy</c>'s
    /// own bounded fields, since they are never recorded to the ledger).</summary>
    internal const int MaximumFieldLength = 900;

    /// <summary>A single implementation attempt edits a bounded set of files; this is a defensive
    /// upper bound against a runaway or malformed response, not a product requirement.</summary>
    public const int MaximumChangedPaths = 200;

    private const int MaximumRelativePathLength = 512;

    private static readonly IReadOnlyList<string> RequiredFields =
        ["summary", "changedRelativePaths", "implementationNotes", "unexpectedDiscoveries", "remainingRisks", "recommendedVerification"];

    public static object BuildSchemaDocument() => new
    {
        type = "object",
        additionalProperties = false,
        required = RequiredFields,
        properties = new Dictionary<string, object>
        {
            ["summary"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
            ["changedRelativePaths"] = new
            {
                type = "array",
                minItems = 0,
                maxItems = MaximumChangedPaths,
                items = new { type = "string", minLength = 1, maxLength = MaximumRelativePathLength },
            },
            ["implementationNotes"] = StringSchema(1, MaximumFieldLength),
            ["unexpectedDiscoveries"] = StringSchema(0, MaximumFieldLength),
            ["remainingRisks"] = StringSchema(0, MaximumFieldLength),
            ["recommendedVerification"] = StringSchema(1, MaximumFieldLength),
        },
    };

    private static object StringSchema(int minLength, int maxLength) => new
    {
        type = "string",
        minLength,
        maxLength,
    };
}
