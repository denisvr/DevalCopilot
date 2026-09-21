using System.Text.Json;

namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// Owns the deliberately small structured-content schema. It is not a provider transcript
/// format: every permitted field is a bounded, decision-relevant summary.
/// </summary>
public static class CollaborationMessageContentPolicy
{
    public const int MaximumSummaryLength = 600;
    public const int MaximumStructuredContentLength = 5000;
    private const int MaximumFieldLength = 900;
    /// <summary>Matches <c>docs/architecture/agent-collaboration-protocol.md</c>'s "Planning"
    /// description exactly: "a proposal with scope, sequence, risks, verification, and
    /// escalation points."</summary>
    private static readonly IReadOnlySet<string> ProposalFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "scope", "implementationSteps", "risks", "verificationPlan", "escalationPoints",
    };
    private static readonly IReadOnlySet<string> AcceptanceFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "rationale",
    };
    private static readonly IReadOnlySet<string> ChallengeFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "disputedItem", "materialImpact", "reasoning", "alternativeOrQuestion",
    };
    private static readonly IReadOnlySet<string> QuestionFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "question", "why",
    };
    private static readonly IReadOnlySet<string> DecisionFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "resolution", "rationale", "resultingPlanChanges", "nextAction",
    };
    private static readonly IReadOnlySet<string> ExecutionReportFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "completedWork", "verification",
    };
    /// <summary>Revised for this stage's first real use (a Codex implementation review's
    /// findings): a closed severity, a closed category, the evidence/rationale for the finding,
    /// and the change required to resolve it. The affected repository-relative path, when the
    /// provider names one, is never duplicated into the ledger — it stays only in the sealed
    /// final-response artifact, exactly like every other bounded free-text field this protocol
    /// keeps out of durable structured content.</summary>
    private static readonly IReadOnlySet<string> ReviewFindingFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "severity", "category", "evidence", "requiredChange",
    };
    private static readonly IReadOnlySet<string> ReviewApprovalFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "rationale", "residualRisks",
    };
    private static readonly IReadOnlySet<string> RevisionResponseFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "disposition", "evidence", "resultingSourceChanges",
    };
    private static readonly IReadOnlySet<string> EscalationFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "unresolvedDecision", "options", "consequences", "evidence", "recommendedChoice",
    };
    private static readonly IReadOnlySet<string> HumanInstructionFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "instruction", "rationale",
    };

    public static void Validate(CollaborationMessageType type, string structuredContentJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(structuredContentJson);

        if (structuredContentJson.Length > MaximumStructuredContentLength)
        {
            throw new ArgumentOutOfRangeException(nameof(structuredContentJson));
        }

        try
        {
            using var document = JsonDocument.Parse(structuredContentJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Structured content must be a JSON object.", nameof(structuredContentJson));
            }

            var expectedFields = GetExpectedFields(type);
            var seenFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!expectedFields.Contains(property.Name) || !seenFields.Add(property.Name))
                {
                    throw new ArgumentException("Structured content contains an unsupported field.", nameof(structuredContentJson));
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new ArgumentException("Structured content fields must be strings.", nameof(structuredContentJson));
                }

                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumFieldLength || LooksUnsafe(value))
                {
                    throw new ArgumentException("Structured content contains an invalid value.", nameof(structuredContentJson));
                }
            }

            if (seenFields.Count != expectedFields.Count)
            {
                throw new ArgumentException("Structured content is missing a required field.", nameof(structuredContentJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Structured content is not valid JSON.", nameof(structuredContentJson), exception);
        }
    }

    public static bool IsSafeSummary(string summary)
    {
        return !string.IsNullOrWhiteSpace(summary) && summary.Length <= MaximumSummaryLength && !LooksUnsafe(summary);
    }

    private static IReadOnlySet<string> GetExpectedFields(CollaborationMessageType type)
    {
        return type switch
        {
            CollaborationMessageType.Proposal => ProposalFields,
            CollaborationMessageType.Acceptance => AcceptanceFields,
            CollaborationMessageType.Challenge => ChallengeFields,
            CollaborationMessageType.Question => QuestionFields,
            CollaborationMessageType.Decision => DecisionFields,
            CollaborationMessageType.ExecutionReport => ExecutionReportFields,
            CollaborationMessageType.ReviewFinding => ReviewFindingFields,
            CollaborationMessageType.RevisionResponse => RevisionResponseFields,
            CollaborationMessageType.Escalation => EscalationFields,
            CollaborationMessageType.ReviewApproval => ReviewApprovalFields,
            CollaborationMessageType.HumanInstruction => HumanInstructionFields,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    private static bool LooksUnsafe(string value)
    {
        var normalized = value.AsSpan().Trim();
        return normalized.Contains(":\\", StringComparison.Ordinal)
            || normalized.Contains("bearer ", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("api_key", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("api key", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("environment", StringComparison.OrdinalIgnoreCase);
    }
}
