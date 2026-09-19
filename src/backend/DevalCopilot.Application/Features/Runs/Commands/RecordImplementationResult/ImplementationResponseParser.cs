using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;

/// <summary>
/// Validates a Claude implementation final response against the exact protocol-owned shape
/// (<see cref="ImplementationReportOutputSchema"/>) — never trusting that the provider actually
/// honored the schema it was given. Unknown fields, a non-object root, a wrong field type, a
/// blank required value, a missing required field, a changed-path count outside its bound, a
/// changed path that is not a safe repository-relative path (absolute, drive-rooted, or
/// containing a parent-directory traversal segment), a duplicated changed path, malformed JSON,
/// or content that would fail <see cref="CollaborationMessage.Record"/>'s own
/// <see cref="CollaborationMessageContentPolicy"/> checks all fail closed to <see langword="null"/>;
/// none of them ever partially populate a result. Mirrors
/// <see cref="DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult.ChallengeResolutionResponseParser"/>
/// exactly. This parser never compares <see cref="ValidatedImplementationReport.ChangedRelativePaths"/>
/// against Git evidence — that exact-set comparison is <c>RecordImplementationResultCommandHandler</c>'s
/// responsibility, since only it has both values in hand.
/// </summary>
public static class ImplementationResponseParser
{
    private static readonly IReadOnlySet<string> TopLevelFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "summary", "changedRelativePaths", "implementationNotes", "unexpectedDiscoveries", "remainingRisks", "recommendedVerification",
    };

    public static ValidatedImplementationReport? TryParse(string finalResponseJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(finalResponseJson);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? summary = null;
            JsonElement? changedRelativePathsElement = null;
            string? implementationNotes = null;
            string? unexpectedDiscoveries = null;
            string? remainingRisks = null;
            string? recommendedVerification = null;
            var seenFields = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in root.EnumerateObject())
            {
                if (!TopLevelFields.Contains(property.Name) || !seenFields.Add(property.Name))
                {
                    return null;
                }

                switch (property.Name)
                {
                    case "summary":
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            return null;
                        }

                        summary = property.Value.GetString();
                        break;
                    case "changedRelativePaths":
                        if (property.Value.ValueKind != JsonValueKind.Array)
                        {
                            return null;
                        }

                        changedRelativePathsElement = property.Value;
                        break;
                    case "implementationNotes":
                        implementationNotes = ReadRequiredString(property.Value);
                        break;
                    case "unexpectedDiscoveries":
                        unexpectedDiscoveries = ReadOptionalString(property.Value);
                        break;
                    case "remainingRisks":
                        remainingRisks = ReadOptionalString(property.Value);
                        break;
                    case "recommendedVerification":
                        recommendedVerification = ReadRequiredString(property.Value);
                        break;
                }
            }

            if (!seenFields.SetEquals(TopLevelFields)
                || string.IsNullOrWhiteSpace(summary)
                || !CollaborationMessageContentPolicy.IsSafeSummary(summary)
                || changedRelativePathsElement is not { } changedRelativePathsArray
                || implementationNotes is null
                || unexpectedDiscoveries is null
                || remainingRisks is null
                || recommendedVerification is null)
            {
                return null;
            }

            if (unexpectedDiscoveries.Length > ImplementationReportOutputSchema.MaximumFieldLength
                || remainingRisks.Length > ImplementationReportOutputSchema.MaximumFieldLength)
            {
                return null;
            }

            var changedRelativePaths = TryParseChangedRelativePaths(changedRelativePathsArray);
            if (changedRelativePaths is null)
            {
                return null;
            }

            if (!ImplementationEvidenceValidation.IsValidExecutionReportContent(implementationNotes, recommendedVerification))
            {
                return null;
            }

            return ValidatedImplementationReport.Create(
                summary, changedRelativePaths, implementationNotes, unexpectedDiscoveries, remainingRisks, recommendedVerification);
        }
    }

    private static string? ReadRequiredString(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Unlike a required field, an empty string is a valid, meaningful answer here
    /// ("nothing unexpected", "no remaining risk") — only a missing or wrong-typed value fails
    /// closed.</summary>
    private static string? ReadOptionalString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string>? TryParseChangedRelativePaths(JsonElement changedRelativePathsArray)
    {
        var itemCount = changedRelativePathsArray.GetArrayLength();
        if (itemCount > ImplementationReportOutputSchema.MaximumChangedPaths)
        {
            return null;
        }

        var paths = new List<string>(itemCount);

        foreach (var item in changedRelativePathsArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } path)
            {
                return null;
            }

            paths.Add(path);
        }

        // Covers a blank, unsafe, over-length, or duplicated path — all fail closed the same
        // way, via the one shared check the recording handler also uses to independently
        // re-validate a report it did not itself parse.
        return ImplementationEvidenceValidation.AreValidChangedRelativePaths(paths, ImplementationReportOutputSchema.MaximumChangedPaths)
            ? paths
            : null;
    }
}
