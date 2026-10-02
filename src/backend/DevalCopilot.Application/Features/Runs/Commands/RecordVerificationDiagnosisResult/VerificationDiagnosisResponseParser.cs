using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisResult;

/// <summary>
/// Validates a Codex verification-diagnosis final response against the exact protocol-owned discriminated
/// Findings-or-Escalation shape (<see cref="VerificationDiagnosisOutputSchema"/>) — never trusting that the provider honored
/// the schema it was given. Unknown or duplicate fields, a non-object root, a non-string value, a blank value, an unrecognized
/// <c>outcome</c>/<c>severity</c>/<c>category</c>, a findings outcome without one to ten findings or with an escalation, an
/// escalation outcome with findings or without exactly the five escalation fields, a duplicate finding, an unsafe or absolute
/// or traversal-shaped affected path, malformed JSON, or content that would fail <see cref="CollaborationMessage.Record"/>'s
/// own content policy all fail closed to <see langword="null"/>. There is no approval shape, so a provider that tries to
/// approve produces nothing.
/// </summary>
public static class VerificationDiagnosisResponseParser
{
    private static readonly IReadOnlyList<string> FindingFields =
        ["severity", "category", "summary", "evidence", "requiredChange", "affectedRelativePath"];

    public static ValidatedVerificationDiagnosis? TryParse(string finalResponseJson)
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
            JsonElement? findingsElement = null;
            JsonElement? escalationElement = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    return null;
                }

                switch (property.Name)
                {
                    case "outcome" when property.Value.ValueKind == JsonValueKind.String:
                        outcome = property.Value.GetString();
                        break;
                    case "summary" when property.Value.ValueKind == JsonValueKind.String:
                        summary = property.Value.GetString();
                        break;
                    case "findings" when property.Value.ValueKind == JsonValueKind.Array:
                        findingsElement = property.Value;
                        break;
                    case "escalation" when property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null:
                        escalationElement = property.Value;
                        break;
                    default:
                        return null;
                }
            }

            if (!seen.SetEquals(["outcome", "summary", "findings", "escalation"])
                || string.IsNullOrWhiteSpace(summary)
                || !CollaborationMessageContentPolicy.IsSafeSummary(summary)
                || findingsElement is not { } findingsArray
                || escalationElement is not { } escalation)
            {
                return null;
            }

            return outcome switch
            {
                VerificationDiagnosisOutputSchema.FindingsOutcome when escalation.ValueKind == JsonValueKind.Null =>
                    TryBuildFindings(summary, findingsArray),
                VerificationDiagnosisOutputSchema.EscalationOutcome when escalation.ValueKind == JsonValueKind.Object
                    && findingsArray.GetArrayLength() == 0 =>
                    TryBuildEscalation(summary, escalation),
                _ => null,
            };
        }
    }

    private static ValidatedVerificationDiagnosis? TryBuildFindings(string summary, JsonElement findingsArray)
    {
        var itemCount = findingsArray.GetArrayLength();
        if (itemCount < VerificationDiagnosisOutputSchema.MinimumFindings || itemCount > VerificationDiagnosisOutputSchema.MaximumFindings)
        {
            return null;
        }

        var findings = new List<ValidatedReviewFinding>(itemCount);
        var seenContent = new HashSet<(string Summary, string Evidence, string RequiredChange)>();
        foreach (var item in findingsArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryReadFinding(item, out var finding))
            {
                return null;
            }

            // A duplicate finding is rejected, never silently collapsed or double-recorded.
            if (!seenContent.Add((finding.Summary, finding.Evidence, finding.RequiredChange)))
            {
                return null;
            }

            findings.Add(finding);
        }

        return ValidatedVerificationDiagnosis.CreateFindings(summary, findings);
    }

    private static ValidatedVerificationDiagnosis? TryBuildEscalation(string summary, JsonElement escalation)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in escalation.EnumerateObject())
        {
            if (!VerificationDiagnosisOutputSchema.EscalationFields.Contains(property.Name)
                || property.Value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(property.Value.GetString())
                || !values.TryAdd(property.Name, property.Value.GetString()!))
            {
                return null;
            }
        }

        if (values.Count != VerificationDiagnosisOutputSchema.EscalationFields.Count)
        {
            return null;
        }

        var parsed = new ValidatedDiagnosisEscalation(
            values["unresolvedDecision"], values["options"], values["consequences"], values["evidence"], values["recommendedChoice"]);
        return IsValidContent(CollaborationMessageType.Escalation, SerializeEscalation(parsed))
            ? ValidatedVerificationDiagnosis.CreateEscalation(summary, parsed)
            : null;
    }

    /// <summary>The exact structured content recorded for an escalation; shared with the recording handler.</summary>
    public static string SerializeEscalation(ValidatedDiagnosisEscalation escalation) => JsonSerializer.Serialize(new
    {
        unresolvedDecision = escalation.UnresolvedDecision,
        options = escalation.Options,
        consequences = escalation.Consequences,
        evidence = escalation.Evidence,
        recommendedChoice = escalation.RecommendedChoice,
    });

    /// <summary>The exact structured content recorded for a finding; shared with the recording handler.</summary>
    public static string SerializeFinding(ValidatedReviewFinding finding) => JsonSerializer.Serialize(new
    {
        severity = finding.Severity,
        category = finding.Category,
        evidence = finding.Evidence,
        requiredChange = finding.RequiredChange,
    });

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

            if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                return false;
            }

            var value = property.Value.GetString();
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

        var candidate = new ValidatedReviewFinding(severity, category, summary, evidence, requiredChange, affectedRelativePath);
        if (!IsValidContent(CollaborationMessageType.ReviewFinding, SerializeFinding(candidate)))
        {
            return false;
        }

        finding = candidate;
        return true;
    }

    /// <summary>Never an absolute path, never a traversal segment, never empty after normalization, and bounded: the one
    /// field this parser accepts that must never leak a local filesystem layout or escape the repository it describes.</summary>
    private static bool IsSafeRepositoryRelativePath(string path)
    {
        if (path.Length > 512 || path.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        if (Path.IsPathRooted(path) || path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal))
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
