using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The closed ADR-0004 field names of every message type, restated independently of the Domain policy under test, with
/// deterministic bounded values.</summary>
internal static class CollaborationHistoryFixtureContent
{
    public static readonly IReadOnlyDictionary<CollaborationMessageType, string[]> FieldNames =
        new Dictionary<CollaborationMessageType, string[]>
        {
            [CollaborationMessageType.Proposal] = ["scope", "implementationSteps", "risks", "verificationPlan", "escalationPoints"],
            [CollaborationMessageType.Acceptance] = ["rationale"],
            [CollaborationMessageType.Challenge] = ["disputedItem", "materialImpact", "reasoning", "alternativeOrQuestion"],
            [CollaborationMessageType.Question] = ["question", "why"],
            [CollaborationMessageType.Decision] = ["resolution", "rationale", "resultingPlanChanges", "nextAction"],
            [CollaborationMessageType.ExecutionReport] = ["completedWork", "verification"],
            [CollaborationMessageType.ReviewFinding] = ["severity", "category", "evidence", "requiredChange"],
            [CollaborationMessageType.RevisionResponse] = ["disposition", "evidence", "resultingSourceChanges"],
            [CollaborationMessageType.Escalation] = ["unresolvedDecision", "options", "consequences", "evidence", "recommendedChoice"],
            [CollaborationMessageType.ReviewApproval] = ["rationale", "residualRisks"],
            [CollaborationMessageType.HumanInstruction] = ["instruction", "rationale"],
        };

    public static string Value(string name, int index = 0) => $"Recorded {name} text {index}.";

    public static string Json(CollaborationMessageType type, int index = 0) => JsonSerializer.Serialize(
        FieldNames[type].ToDictionary(name => name, name => Value(name, index)));
}
