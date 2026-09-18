using System.Text.Json;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;

/// <summary>
/// Validates a Codex challenge-resolution final response against the exact protocol-owned
/// Decision-set-plus-revised-Proposal shape (<see cref="ChallengeResolutionOutputSchema"/>) —
/// never trusting that the provider actually honored the schema it was given, and never trusting
/// that the reported <c>challengeMessageId</c> values are the exact input Challenge set. Unknown
/// fields, a non-object root, a non-string field value, a blank value, a missing required field,
/// an unrecognized <c>resolution</c>, a decision count outside its bound, a malformed
/// <c>challengeMessageId</c>, a missing/duplicate/foreign/invented/unhandled Challenge identifier,
/// malformed JSON, or content that would fail <see cref="CollaborationMessage.Record"/>'s own
/// <see cref="CollaborationMessageContentPolicy"/> checks (excessive length, unsafe-looking
/// content) all fail closed to <see langword="null"/>; none of them ever partially populate a
/// result — mirrors <see cref="DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult.ClaudeCriticalReviewResponseParser"/>
/// exactly.
/// </summary>
public static class ChallengeResolutionResponseParser
{
    private static readonly IReadOnlyList<string> DecisionItemFields =
        ["challengeMessageId", "summary", "resolution", "rationale", "resultingPlanChanges", "nextAction"];

    private static readonly IReadOnlyList<string> RevisedProposalFields =
        ["summary", "scope", "implementationSteps", "risks", "verificationPlan", "escalationPoints"];

    /// <summary>
    /// <paramref name="expectedChallengeMessageIds"/> is the exact, closed set of input Challenge
    /// message identifiers this attempt was claimed to resolve — never inferred from the
    /// response itself. Every one of them must appear exactly once among the reported decisions,
    /// and no other identifier may appear.
    /// </summary>
    public static ValidatedChallengeResolution? TryParse(
        string finalResponseJson, IReadOnlySet<Guid> expectedChallengeMessageIds)
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
            JsonElement? decisionsElement = null;
            JsonElement? revisedProposalElement = null;
            var seenTopLevelFields = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in root.EnumerateObject())
            {
                if (!seenTopLevelFields.Add(property.Name))
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
                    case "decisions":
                        if (property.Value.ValueKind != JsonValueKind.Array)
                        {
                            return null;
                        }

                        decisionsElement = property.Value;
                        break;
                    case "revisedProposal":
                        if (property.Value.ValueKind != JsonValueKind.Object)
                        {
                            return null;
                        }

                        revisedProposalElement = property.Value;
                        break;
                    default:
                        return null;
                }
            }

            if (!seenTopLevelFields.SetEquals(["summary", "decisions", "revisedProposal"])
                || string.IsNullOrWhiteSpace(summary)
                || !CollaborationMessageContentPolicy.IsSafeSummary(summary)
                || decisionsElement is not { } decisionsArray
                || revisedProposalElement is not { } revisedProposalObject)
            {
                return null;
            }

            var decisions = TryParseDecisions(decisionsArray, expectedChallengeMessageIds);
            if (decisions is null)
            {
                return null;
            }

            var revisedProposal = TryParseRevisedProposal(revisedProposalObject);
            if (revisedProposal is null)
            {
                return null;
            }

            return ValidatedChallengeResolution.Create(summary, decisions, revisedProposal);
        }
    }

    private static IReadOnlyList<ValidatedDecision>? TryParseDecisions(
        JsonElement decisionsArray, IReadOnlySet<Guid> expectedChallengeMessageIds)
    {
        var itemCount = decisionsArray.GetArrayLength();
        if (itemCount < ChallengeResolutionOutputSchema.MinimumDecisions || itemCount > ChallengeResolutionOutputSchema.MaximumDecisions)
        {
            return null;
        }

        var decisions = new List<ValidatedDecision>(itemCount);
        var seenChallengeMessageIds = new HashSet<Guid>();

        foreach (var item in decisionsArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryReadExactStringFields(item, DecisionItemFields, out var values))
            {
                return null;
            }

            if (!Guid.TryParse(values["challengeMessageId"], out var challengeMessageId)
                || !expectedChallengeMessageIds.Contains(challengeMessageId)
                || !seenChallengeMessageIds.Add(challengeMessageId))
            {
                // Covers a malformed identifier, a foreign or invented identifier (not in the
                // exact claimed Challenge set), and a duplicate — all fail closed the same way.
                return null;
            }

            var resolution = values["resolution"];
            if (resolution != ChallengeResolutionOutputSchema.AcceptedResolution
                && resolution != ChallengeResolutionOutputSchema.PartiallyAcceptedResolution
                && resolution != ChallengeResolutionOutputSchema.RejectedResolution)
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
                resolution,
                rationale = values["rationale"],
                resultingPlanChanges = values["resultingPlanChanges"],
                nextAction = values["nextAction"],
            });
            if (!IsValidContent(CollaborationMessageType.Decision, structuredContentJson))
            {
                return null;
            }

            decisions.Add(new ValidatedDecision(challengeMessageId, itemSummary, structuredContentJson));
        }

        // Every input Challenge must be resolved — never fewer than the exact claimed set, and
        // the loop above already rejected any identifier outside it, so set-equality here also
        // proves there are no missing (unhandled) Challenges.
        return seenChallengeMessageIds.SetEquals(expectedChallengeMessageIds) ? decisions : null;
    }

    private static ValidatedRevisedProposal? TryParseRevisedProposal(JsonElement revisedProposalObject)
    {
        if (!TryReadExactStringFields(revisedProposalObject, RevisedProposalFields, out var values))
        {
            return null;
        }

        var summary = values["summary"];
        if (!CollaborationMessageContentPolicy.IsSafeSummary(summary))
        {
            return null;
        }

        var structuredContentJson = JsonSerializer.Serialize(new
        {
            scope = values["scope"],
            implementationSteps = values["implementationSteps"],
            risks = values["risks"],
            verificationPlan = values["verificationPlan"],
            escalationPoints = values["escalationPoints"],
        });
        if (!IsValidContent(CollaborationMessageType.Proposal, structuredContentJson))
        {
            return null;
        }

        return new ValidatedRevisedProposal(summary, structuredContentJson);
    }

    /// <summary>Every field in <paramref name="expectedFields"/> must appear exactly once, as a
    /// non-blank string, and no other field may appear. Mirrors
    /// <c>ClaudeCriticalReviewResponseParser</c>'s identically named helper exactly.</summary>
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
