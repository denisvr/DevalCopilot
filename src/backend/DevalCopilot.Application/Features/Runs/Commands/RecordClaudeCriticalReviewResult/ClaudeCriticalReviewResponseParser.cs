using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;

/// <summary>
/// Validates a Claude critical-review final response against the exact protocol-owned
/// Acceptance/Challenge union (<see cref="ClaudeCriticalReviewOutputSchema"/>) — never trusting
/// that the provider actually honored the schema it was given. Unknown fields, a non-object root,
/// a non-string field value, a blank value, a missing required field, an unrecognized or missing
/// <c>decision</c>, a challenge count outside 1..5, malformed JSON, or content that would fail
/// <see cref="CollaborationMessage.Record"/>'s own <see cref="CollaborationMessageContentPolicy"/>
/// checks (excessive length, unsafe-looking content) all fail closed to <see langword="null"/>;
/// none of them ever partially populate a result — mirrors <c>CodexFinalResponseParser</c> exactly.
/// </summary>
public static class ClaudeCriticalReviewResponseParser
{
    private static readonly IReadOnlyList<string> ChallengeItemFields =
        ["summary", "disputedItem", "materialImpact", "reasoning", "alternativeOrQuestion"];

    public static ValidatedCriticalReview? TryParse(string finalResponseJson)
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
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("decision", out var decisionElement)
                || decisionElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return decisionElement.GetString() switch
            {
                ClaudeCriticalReviewOutputSchema.AcceptDecision => TryParseAcceptance(root),
                ClaudeCriticalReviewOutputSchema.ChallengeDecision => TryParseChallenges(root),
                _ => null,
            };
        }
    }

    private static ValidatedCriticalReview? TryParseAcceptance(JsonElement root)
    {
        if (!TryReadExactStringFields(root, ["decision", "summary", "rationale"], out var values))
        {
            return null;
        }

        var summary = values["summary"];
        if (!CollaborationMessageContentPolicy.IsSafeSummary(summary))
        {
            return null;
        }

        var structuredContentJson = JsonSerializer.Serialize(new { rationale = values["rationale"] });
        if (!IsValidContent(CollaborationMessageType.Acceptance, structuredContentJson))
        {
            return null;
        }

        return ValidatedCriticalReview.ForAcceptance(new ValidatedAcceptance(summary, structuredContentJson));
    }

    private static ValidatedCriticalReview? TryParseChallenges(JsonElement root)
    {
        var seenFields = new HashSet<string>(StringComparer.Ordinal);
        string? challengeSetSummary = null;
        JsonElement? challengesElement = null;

        foreach (var property in root.EnumerateObject())
        {
            if (!seenFields.Add(property.Name))
            {
                return null;
            }

            switch (property.Name)
            {
                case "decision":
                    break;
                case "summary":
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }

                    challengeSetSummary = property.Value.GetString();
                    break;
                case "challenges":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        return null;
                    }

                    challengesElement = property.Value;
                    break;
                default:
                    return null;
            }
        }

        if (!seenFields.SetEquals(["decision", "summary", "challenges"])
            || string.IsNullOrWhiteSpace(challengeSetSummary)
            || !CollaborationMessageContentPolicy.IsSafeSummary(challengeSetSummary)
            || challengesElement is not { } elements)
        {
            return null;
        }

        var itemCount = elements.GetArrayLength();
        if (itemCount < ClaudeCriticalReviewOutputSchema.MinimumChallenges || itemCount > ClaudeCriticalReviewOutputSchema.MaximumChallenges)
        {
            return null;
        }

        var challenges = new List<ValidatedChallenge>(itemCount);
        foreach (var item in elements.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryReadExactStringFields(item, ChallengeItemFields, out var values))
            {
                return null;
            }

            var itemSummary = values["summary"];
            if (!CollaborationMessageContentPolicy.IsSafeSummary(itemSummary))
            {
                return null;
            }

            var structuredContentJson = JsonSerializer.Serialize(new
            {
                disputedItem = values["disputedItem"],
                materialImpact = values["materialImpact"],
                reasoning = values["reasoning"],
                alternativeOrQuestion = values["alternativeOrQuestion"],
            });
            if (!IsValidContent(CollaborationMessageType.Challenge, structuredContentJson))
            {
                return null;
            }

            challenges.Add(new ValidatedChallenge(itemSummary, structuredContentJson));
        }

        return ValidatedCriticalReview.ForChallenges(challenges, challengeSetSummary);
    }

    /// <summary>Every field in <paramref name="expectedFields"/> must appear exactly once, as a
    /// non-blank string, and no other field may appear.</summary>
    private static bool TryReadExactStringFields(
        JsonElement obj, IReadOnlyList<string> expectedFields, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        var expected = new HashSet<string>(expectedFields, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in obj.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !seen.Add(property.Name) || property.Value.ValueKind != JsonValueKind.String)
            {
                values = new Dictionary<string, string>(StringComparer.Ordinal);
                return false;
            }

            var value = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                values = new Dictionary<string, string>(StringComparer.Ordinal);
                return false;
            }

            values[property.Name] = value;
        }

        return seen.Count == expected.Count;
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
