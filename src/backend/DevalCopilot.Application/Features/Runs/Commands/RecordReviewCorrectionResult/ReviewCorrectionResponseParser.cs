using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionResult;

/// <summary>Parses the strict ReviewCorrection result and requires the exact ordered finding ids
/// supplied by the durable attempt input identity. Invalid JSON or null means no structured result;
/// a non-null but malformed object is never partially accepted.</summary>
public static class ReviewCorrectionResponseParser
{
    private static readonly IReadOnlySet<string> RevisionFields =
        new HashSet<string>(["findingMessageId", "disposition", "evidence", "resultingSourceChanges"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> ReportFields =
        new HashSet<string>(["summary", "changedRelativePaths", "implementationNotes", "unexpectedDiscoveries", "remainingRisks", "recommendedVerification"], StringComparer.Ordinal);

    public static ValidatedReviewCorrection? TryParse(string finalResponseJson, IReadOnlyList<Guid> expectedFindingMessageIds)
    {
        try
        {
            using var document = JsonDocument.Parse(finalResponseJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = document.RootElement;
            if (!TryGetUniquePropertyNames(root, ["revisionResponses", "executionReport"], out var properties)
                || properties["revisionResponses"].ValueKind != JsonValueKind.Array
                || properties["executionReport"].ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var revisions = new List<ValidatedRevisionResponse>();
            var revisionArray = properties["revisionResponses"];
            if (revisionArray.GetArrayLength() != expectedFindingMessageIds.Count)
            {
                return null;
            }

            for (var index = 0; index < revisionArray.GetArrayLength(); index++)
            {
                if (!TryReadRevision(revisionArray[index], expectedFindingMessageIds[index], out var revision))
                {
                    return null;
                }

                revisions.Add(revision);
            }

            var report = TryReadReport(properties["executionReport"]);
            return report is null ? null : ValidatedReviewCorrection.Create(revisions, report);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryGetUniquePropertyNames(
        JsonElement objectElement,
        IReadOnlyList<string> expectedNames,
        out Dictionary<string, JsonElement> properties)
    {
        properties = new(StringComparer.Ordinal);
        var expected = expectedNames.ToHashSet(StringComparer.Ordinal);
        foreach (var property in objectElement.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !properties.TryAdd(property.Name, property.Value))
            {
                return false;
            }
        }

        return properties.Count == expected.Count;
    }

    private static bool TryReadRevision(JsonElement element, Guid expectedFindingMessageId, out ValidatedRevisionResponse revision)
    {
        revision = null!;
        if (element.ValueKind != JsonValueKind.Object || !TryGetUniquePropertyNames(element, RevisionFields.ToArray(), out var properties))
        {
            return false;
        }

        if (properties["findingMessageId"].ValueKind != JsonValueKind.String
            || !Guid.TryParse(properties["findingMessageId"].GetString(), out var findingMessageId)
            || findingMessageId != expectedFindingMessageId)
        {
            return false;
        }

        if (!TryReadBoundedString(properties["disposition"], out var disposition, CollaborationMessageContentPolicy.MaximumSummaryLength)
            || !TryReadBoundedString(properties["evidence"], out var evidence)
            || !TryReadBoundedString(properties["resultingSourceChanges"], out var resultingSourceChanges))
        {
            return false;
        }

        var content = JsonSerializer.Serialize(new { disposition, evidence, resultingSourceChanges });
        if (!IsValidContent(CollaborationMessageType.RevisionResponse, content))
        {
            return false;
        }

        revision = new ValidatedRevisionResponse(findingMessageId, disposition, evidence, resultingSourceChanges);
        return true;
    }

    private static ValidatedImplementationReport? TryReadReport(JsonElement element)
    {
        if (!TryGetUniquePropertyNames(element, ReportFields.ToArray(), out var properties)
            || properties["changedRelativePaths"].ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        if (!TryReadBoundedString(properties["summary"], out var summary)
            || !TryReadBoundedString(properties["implementationNotes"], out var implementationNotes)
            || !TryReadAllowEmptyString(properties["unexpectedDiscoveries"], out var unexpectedDiscoveries)
            || !TryReadAllowEmptyString(properties["remainingRisks"], out var remainingRisks)
            || !TryReadBoundedString(properties["recommendedVerification"], out var recommendedVerification))
        {
            return null;
        }

        var paths = new List<string>();
        foreach (var pathElement in properties["changedRelativePaths"].EnumerateArray())
        {
            if (pathElement.ValueKind != JsonValueKind.String || !ImplementationEvidenceValidation.IsSafeRepositoryRelativePath(pathElement.GetString()!))
            {
                return null;
            }

            paths.Add(pathElement.GetString()!);
        }

        if (!ImplementationEvidenceValidation.AreValidChangedRelativePaths(paths, ImplementationReportOutputSchema.MaximumChangedPaths))
        {
            return null;
        }

        var report = ValidatedImplementationReport.Create(
            summary, paths, implementationNotes, unexpectedDiscoveries, remainingRisks, recommendedVerification);
        return ImplementationReportValidation.IsValid(report) ? report : null;
    }

    private static bool TryReadBoundedString(JsonElement element, out string value, int maximumLength = ReviewCorrectionOutputSchema.MaximumFieldLength)
    {
        value = string.Empty;
        return element.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value = element.GetString()!)
            && value.Length <= maximumLength;
    }

    private static bool TryReadAllowEmptyString(JsonElement element, out string value)
    {
        value = string.Empty;
        return element.ValueKind == JsonValueKind.String
            && (value = element.GetString()!) is not null
            && value.Length <= ReviewCorrectionOutputSchema.MaximumFieldLength;
    }

    private static bool IsValidContent(CollaborationMessageType type, string content)
    {
        try
        {
            CollaborationMessageContentPolicy.Validate(type, content);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
