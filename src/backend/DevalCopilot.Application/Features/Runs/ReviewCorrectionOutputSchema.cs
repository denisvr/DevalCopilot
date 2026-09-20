using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>The closed response schema for one ReviewCorrection attempt. Finding ids are echoed
/// from the manifest so the boundary can prove exact one-to-one binding.</summary>
public static class ReviewCorrectionOutputSchema
{
    public const int MaximumFindings = 10;
    public const int MaximumFieldLength = 900;
    public const int MaximumRelativePathLength = 512;

    private static readonly IReadOnlyList<string> RevisionFields =
        ["findingMessageId", "disposition", "evidence", "resultingSourceChanges"];

    private static readonly IReadOnlyList<string> ReportFields =
        ["summary", "changedRelativePaths", "implementationNotes", "unexpectedDiscoveries", "remainingRisks", "recommendedVerification"];

    public static object BuildSchemaDocument() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "revisionResponses", "executionReport" },
        properties = new Dictionary<string, object>
        {
            ["revisionResponses"] = new
            {
                type = "array",
                minItems = 1,
                maxItems = MaximumFindings,
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = RevisionFields,
                    properties = new Dictionary<string, object>
                    {
                        ["findingMessageId"] = new { type = "string", format = "uuid" },
                        ["disposition"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
                        ["evidence"] = StringSchema(1, MaximumFieldLength),
                        ["resultingSourceChanges"] = StringSchema(1, MaximumFieldLength),
                    },
                },
            },
            ["executionReport"] = new
            {
                type = "object",
                additionalProperties = false,
                required = ReportFields,
                properties = new Dictionary<string, object>
                {
                    ["summary"] = StringSchema(1, CollaborationMessageContentPolicy.MaximumSummaryLength),
                    ["changedRelativePaths"] = new
                    {
                        type = "array",
                        minItems = 0,
                        maxItems = ImplementationReportOutputSchema.MaximumChangedPaths,
                        items = new { type = "string", minLength = 1, maxLength = MaximumRelativePathLength },
                    },
                    ["implementationNotes"] = StringSchema(1, MaximumFieldLength),
                    ["unexpectedDiscoveries"] = StringSchema(0, MaximumFieldLength),
                    ["remainingRisks"] = StringSchema(0, MaximumFieldLength),
                    ["recommendedVerification"] = StringSchema(1, MaximumFieldLength),
                },
            },
        },
    };

    private static object StringSchema(int minLength, int maxLength) => new { type = "string", minLength, maxLength };
}
