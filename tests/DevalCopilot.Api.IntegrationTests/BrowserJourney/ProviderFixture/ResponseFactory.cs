using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>
/// Deterministic protocol responses in the exact shapes the production schemas require. Identities that must round-trip
/// (challenges, findings) come from the real manifest; everything else is fixed text that avoids the words and path forms the
/// collaboration content policy rejects. These are fixture reports, not evidence of what a real provider would answer.
/// </summary>
public static class ResponseFactory
{
    /// <summary>The summary and content of the one Decision of the final (second) resolution. The authorized implementation checks
    /// that the Decisions it was given are exactly these, so an earlier round's Decision can never stand in for them.</summary>
    public const string FinalDecisionSummary = "Accept the operand-order challenge.";

    private const string FinalDecisionResolution = "accepted";
    private const string FinalDecisionRationale = "Naming that either operand order is acceptable removes the ambiguity.";
    private const string FinalDecisionPlanChanges = "The plan now replaces only the return statement of Total.";
    private const string FinalDecisionNextAction = "Implement the final plan once a human authorizes it.";

    public static bool IsFinalDecisionContent(JsonElement content) =>
        Text(content, "resolution") == FinalDecisionResolution
        && Text(content, "rationale") == FinalDecisionRationale
        && Text(content, "resultingPlanChanges") == FinalDecisionPlanChanges
        && Text(content, "nextAction") == FinalDecisionNextAction;

    private static string? Text(JsonElement content, string name) =>
        content.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static string RootProposal() => Serialize(new JsonObject
    {
        ["summary"] = "Rebuild the ledger total through a lookup table.",
        ["scope"] = "Replace the arithmetic with a lookup table in a new file named Legacy.",
        ["implementationSteps"] = ManifestInfo.RootPlanMarker + ": create a new Legacy file holding a precomputed table and route every total through it.",
        ["risks"] = "A large table is hard to keep consistent with the real calculation.",
        ["verificationPlan"] = "Run the configured verification command.",
        ["escalationPoints"] = "None expected.",
    });

    /// <summary>The first critical review, of the Planner root.</summary>
    public static string Challenge() => Serialize(new JsonObject
    {
        ["decision"] = "challenge",
        ["summary"] = "The root plan duplicates the calculation in a second file.",
        ["challenges"] = new JsonArray(new JsonObject
        {
            ["summary"] = "The lookup table duplicates the arithmetic.",
            ["disputedItem"] = "Step one creates a second file for the total.",
            ["materialImpact"] = "A second source of truth would drift from the real total.",
            ["reasoning"] = "The total is already computed in the Feature file, so a table only adds a place to be wrong.",
            ["alternativeOrQuestion"] = "Implement the calculation in the Feature file instead.",
        }),
    });

    /// <summary>The second critical review, of the first revision: a materially different concern from the first.</summary>
    public static string SecondChallenge() => Serialize(new JsonObject
    {
        ["decision"] = "challenge",
        ["summary"] = "The revised plan leaves the operand order unspecified.",
        ["challenges"] = new JsonArray(new JsonObject
        {
            ["summary"] = "The revised plan never says whether operand order matters.",
            ["disputedItem"] = "The step says only that Total returns the sum of its operands.",
            ["materialImpact"] = "An implementer cannot tell whether reordering the operands is an acceptable edit.",
            ["reasoning"] = "Integer addition does not depend on operand order, so the plan should say so and keep the edit to one statement.",
            ["alternativeOrQuestion"] = "State that either operand order is acceptable and that only the return statement changes.",
        }),
    });

    /// <summary>The first resolution, of the Planner root: the first revision.</summary>
    public static string Resolution(IReadOnlyList<Guid> challengeIds) => Resolve(
        challengeIds,
        "Accept the challenge.",
        "A lookup table would duplicate the real calculation.",
        "The scope moves to the Feature file.",
        "Implement the revised plan.",
        new JsonObject
        {
            ["summary"] = "Implement the total in the Feature file.",
            ["scope"] = "Make Total in src/Feature.cs add its two operands and touch no other file.",
            ["implementationSteps"] = ManifestInfo.RevisedPlanMarker + ": edit src/Feature.cs so Total returns the sum of left and right.",
            ["risks"] = "A wrong edit changes every total.",
            ["verificationPlan"] = "Run the configured verification command against the edited file.",
            ["escalationPoints"] = "None expected.",
        });

    /// <summary>The second and last resolution, of the first revision: the final revision, distinguishable from both earlier plans.</summary>
    public static string FinalResolution(IReadOnlyList<Guid> challengeIds) => Resolve(
        challengeIds,
        FinalDecisionSummary,
        FinalDecisionRationale,
        FinalDecisionPlanChanges,
        FinalDecisionNextAction,
        new JsonObject
        {
            ["summary"] = "Implement the total as one return statement.",
            ["scope"] = "Replace only the return statement of Total in src/Feature.cs with one that returns the sum, and touch no other file.",
            ["implementationSteps"] = ManifestInfo.FinalPlanMarker + ": replace only the return statement of Total in src/Feature.cs so it returns left plus right.",
            ["risks"] = "A wrong edit changes every total.",
            ["verificationPlan"] = "Run the configured verification command against the single edited statement.",
            ["escalationPoints"] = "None expected.",
        });

    private static string Resolve(
        IReadOnlyList<Guid> challengeIds,
        string summary,
        string rationale,
        string planChanges,
        string nextAction,
        JsonObject revisedProposal)
    {
        var decisions = new JsonArray();
        foreach (var id in challengeIds)
        {
            decisions.Add(new JsonObject
            {
                ["challengeMessageId"] = id.ToString(),
                ["summary"] = summary,
                ["resolution"] = "accepted",
                ["rationale"] = rationale,
                ["resultingPlanChanges"] = planChanges,
                ["nextAction"] = nextAction,
            });
        }

        return Serialize(new JsonObject
        {
            ["summary"] = "The challenge is accepted and the plan is narrowed.",
            ["decisions"] = decisions,
            ["revisedProposal"] = revisedProposal,
        });
    }

    /// <summary>The report names the plan the implementation was given, so the recorded report is distinguishable per plan.</summary>
    public static string ImplementationReport(string planMarker) => Serialize(new JsonObject
    {
        ["summary"] = "Implemented Total in the Feature file.",
        ["changedRelativePaths"] = new JsonArray(OwnedLocation.CandidateRelativePath),
        ["implementationNotes"] = planMarker == ManifestInfo.FinalPlanMarker
            ? "Implemented the final plan in the single named file."
            : "Implemented the revised plan in the single named file.",
        ["unexpectedDiscoveries"] = "None.",
        ["remainingRisks"] = "Local verification has not been run.",
        ["recommendedVerification"] = "Run the configured verification command.",
    });

    public static string DiagnosisFindings() => Serialize(new JsonObject
    {
        ["outcome"] = "findings",
        ["summary"] = "The local verification failed on the total calculation.",
        ["findings"] = new JsonArray(new JsonObject
        {
            ["severity"] = "high",
            ["category"] = "correctness",
            ["summary"] = "Total does not add its operands.",
            ["evidence"] = "The failed verification output reports that Total does not add left and right.",
            ["requiredChange"] = "Make Total in the Feature file add left and right.",
            ["affectedRelativePath"] = OwnedLocation.CandidateRelativePath,
        }),
        ["escalation"] = null,
    });

    public static string Correction(IReadOnlyList<Guid> findingIds)
    {
        var responses = new JsonArray();
        foreach (var id in findingIds)
        {
            responses.Add(new JsonObject
            {
                ["findingMessageId"] = id.ToString(),
                ["disposition"] = "Fixed",
                ["evidence"] = "The operands are now added.",
                ["resultingSourceChanges"] = "Total in the Feature file adds left and right.",
            });
        }

        return Serialize(new JsonObject
        {
            ["revisionResponses"] = responses,
            ["executionReport"] = new JsonObject
            {
                ["summary"] = "Correction complete.",
                ["changedRelativePaths"] = new JsonArray(OwnedLocation.CandidateRelativePath),
                ["implementationNotes"] = "Restored addition in Total.",
                ["unexpectedDiscoveries"] = "None.",
                ["remainingRisks"] = "Verification has not been rerun.",
                ["recommendedVerification"] = "Run the configured verification command.",
            },
        });
    }

    /// <summary>The approval names the plan it judged, so the recorded approval is distinguishable per plan.</summary>
    public static string ReviewApproved(string planMarker) => Serialize(new JsonObject
    {
        ["outcome"] = "approved",
        ["summary"] = planMarker == ManifestInfo.FinalPlanMarker
            ? "The implementation matches the final plan."
            : "The implementation matches the revised plan.",
        ["rationale"] = "Total adds its operands and the local verification passed.",
        ["residualRisks"] = "None material.",
        ["findings"] = new JsonArray(),
    });

    private static string Serialize(JsonObject value) => value.ToJsonString(new JsonSerializerOptions());
}
