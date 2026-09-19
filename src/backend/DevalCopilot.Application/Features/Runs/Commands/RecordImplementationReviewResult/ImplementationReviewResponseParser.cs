using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;

/// <summary>
/// Validates a Codex implementation-review final response against the exact protocol-owned
/// discriminated Approved-or-ChangesRequested shape (<see cref="ImplementationReviewOutputSchema"/>) —
/// never trusting that the provider actually honored the schema it was given. Unknown fields, a
/// non-object root, a non-string field value, a blank value, a missing required field, an
/// unrecognized <c>outcome</c>/<c>severity</c>/<c>category</c>, an Approved response that also
/// carries findings, a ChangesRequested response that also carries rationale/residualRisks, a
/// finding count outside its bound, a duplicate finding, an unsafe or absolute or traversal-shaped
/// affected path, malformed JSON, or content that would fail <see cref="CollaborationMessage.Record"/>'s
/// own <see cref="CollaborationMessageContentPolicy"/> checks all fail closed to
/// <see langword="null"/>; none of them ever partially populate a result — mirrors
/// <see cref="DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult.ChallengeResolutionResponseParser"/>
/// exactly.
/// </summary>
public static class ImplementationReviewResponseParser
{
    private static readonly IReadOnlyList<string> FindingFields =
        ["severity", "category", "summary", "evidence", "requiredChange", "affectedRelativePath"];

    public static ValidatedImplementationReview? TryParse(string finalResponseJson)
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

            string? outcome = null;
            string? summary = null;
            string? rationale = null;
            bool rationalePresent = false;
            string? residualRisks = null;
            bool residualRisksPresent = false;
            JsonElement? findingsElement = null;
            var seenTopLevelFields = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in root.EnumerateObject())
            {
                if (!seenTopLevelFields.Add(property.Name))
                {
                    return null;
                }

                switch (property.Name)
                {
                    case "outcome":
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            return null;
                        }

                        outcome = property.Value.GetString();
                        break;
                    case "summary":
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            return null;
                        }

                        summary = property.Value.GetString();
                        break;
                    case "rationale":
                        rationalePresent = true;
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            rationale = property.Value.GetString();
                        }
                        else if (property.Value.ValueKind != JsonValueKind.Null)
                        {
                            return null;
                        }

                        break;
                    case "residualRisks":
                        residualRisksPresent = true;
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            residualRisks = property.Value.GetString();
                        }
                        else if (property.Value.ValueKind != JsonValueKind.Null)
                        {
                            return null;
                        }

                        break;
                    case "findings":
                        if (property.Value.ValueKind != JsonValueKind.Array)
                        {
                            return null;
                        }

                        findingsElement = property.Value;
                        break;
                    default:
                        return null;
                }
            }

            if (!seenTopLevelFields.SetEquals(["outcome", "summary", "rationale", "residualRisks", "findings"])
                || !rationalePresent
                || !residualRisksPresent
                || string.IsNullOrWhiteSpace(summary)
                || !CollaborationMessageContentPolicy.IsSafeSummary(summary)
                || findingsElement is not { } findingsArray)
            {
                return null;
            }

            return outcome switch
            {
                ImplementationReviewOutputSchema.ApprovedOutcome =>
                    TryBuildApproved(summary, rationale, residualRisks, findingsArray),
                ImplementationReviewOutputSchema.ChangesRequestedOutcome =>
                    TryBuildChangesRequested(summary, rationale, residualRisks, findingsArray),
                _ => null,
            };
        }
    }

    private static ValidatedImplementationReview? TryBuildApproved(
        string summary, string? rationale, string? residualRisks, JsonElement findingsArray)
    {
        // Approved never carries findings and always carries a real rationale/residualRisks —
        // any other combination is a mixed or incoherent response, never coerced into a shape.
        if (findingsArray.GetArrayLength() != 0
            || string.IsNullOrWhiteSpace(rationale)
            || string.IsNullOrWhiteSpace(residualRisks)
            || !IsValidContent(CollaborationMessageType.ReviewApproval, JsonSerializer.Serialize(new { rationale, residualRisks })))
        {
            return null;
        }

        return ValidatedImplementationReview.CreateApproved(summary, rationale, residualRisks);
    }

    private static ValidatedImplementationReview? TryBuildChangesRequested(
        string summary, string? rationale, string? residualRisks, JsonElement findingsArray)
    {
        // ChangesRequested never carries an approval rationale/residualRisks — mixing the two
        // shapes in one response is always rejected, never silently reconciled.
        if (rationale is not null || residualRisks is not null)
        {
            return null;
        }

        var findings = TryParseFindings(findingsArray);
        return findings is null ? null : ValidatedImplementationReview.CreateChangesRequested(summary, findings);
    }

    private static IReadOnlyList<ValidatedReviewFinding>? TryParseFindings(JsonElement findingsArray)
    {
        var itemCount = findingsArray.GetArrayLength();
        if (itemCount < ImplementationReviewOutputSchema.MinimumFindings || itemCount > ImplementationReviewOutputSchema.MaximumFindings)
        {
            return null;
        }

        var findings = new List<ValidatedReviewFinding>(itemCount);
        var seenExactContent = new HashSet<(string Summary, string Evidence, string RequiredChange)>();

        foreach (var item in findingsArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryReadFinding(item, out var finding))
            {
                return null;
            }

            // A duplicate finding — the exact same summary/evidence/requiredChange reported more
            // than once — is always rejected, never silently collapsed or double-recorded.
            if (!seenExactContent.Add((finding.Summary, finding.Evidence, finding.RequiredChange)))
            {
                return null;
            }

            findings.Add(finding);
        }

        return findings;
    }

    private static bool TryReadFinding(JsonElement item, out ValidatedReviewFinding finding)
    {
        finding = null!;

        string? severity = null;
        string? category = null;
        string? summary = null;
        string? evidence = null;
        string? requiredChange = null;
        string? affectedRelativePath = null;
        var seenFields = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in item.EnumerateObject())
        {
            if (!FindingFields.Contains(property.Name) || !seenFields.Add(property.Name))
            {
                return false;
            }

            if (property.Name == "affectedRelativePath")
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    affectedRelativePath = property.Value.GetString();
                    if (string.IsNullOrWhiteSpace(affectedRelativePath) || !IsSafeRepositoryRelativePath(affectedRelativePath))
                    {
                        return false;
                    }
                }
                else if (property.Value.ValueKind != JsonValueKind.Null)
                {
                    return false;
                }

                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var value = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (property.Name)
            {
                case "severity":
                    severity = value;
                    break;
                case "category":
                    category = value;
                    break;
                case "summary":
                    summary = value;
                    break;
                case "evidence":
                    evidence = value;
                    break;
                case "requiredChange":
                    requiredChange = value;
                    break;
            }
        }

        if (!seenFields.SetEquals(FindingFields)
            || severity is null || !ImplementationReviewOutputSchema.Severities.Contains(severity)
            || category is null || !ImplementationReviewOutputSchema.Categories.Contains(category)
            || summary is null || !CollaborationMessageContentPolicy.IsSafeSummary(summary)
            || evidence is null || requiredChange is null)
        {
            return false;
        }

        var structuredContentJson = JsonSerializer.Serialize(new { severity, category, evidence, requiredChange });
        if (!IsValidContent(CollaborationMessageType.ReviewFinding, structuredContentJson))
        {
            return false;
        }

        finding = new ValidatedReviewFinding(severity, category, summary, evidence, requiredChange, affectedRelativePath);
        return true;
    }

    /// <summary>Never an absolute path (rooted, drive-qualified, or leading slash), never a
    /// traversal segment, never empty after normalization, and bounded — this is the one field
    /// this parser accepts that must never leak a local filesystem layout or escape the
    /// repository it describes.</summary>
    private static bool IsSafeRepositoryRelativePath(string path)
    {
        if (path.Length > 512 || path.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        if (System.IO.Path.IsPathRooted(path) || path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.None);
        return segments.Length > 0 && segments.All(segment => segment.Length > 0 && segment != "." && segment != "..");
    }

    private static bool IsValidContent(CollaborationMessageType type, string structuredContentJson)
    {
        try
        {
            CollaborationMessageContentPolicy.Validate(type, structuredContentJson);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
