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
    public static string RootProposal() => Serialize(new JsonObject
    {
        ["summary"] = "Rebuild the ledger total through a lookup table.",
        ["scope"] = "Replace the arithmetic with a lookup table in a new file named Legacy.",
        ["implementationSteps"] = ManifestInfo.RootPlanMarker + ": create a new Legacy file holding a precomputed table and route every total through it.",
        ["risks"] = "A large table is hard to keep consistent with the real calculation.",
        ["verificationPlan"] = "Run the configured verification command.",
        ["escalationPoints"] = "None expected.",
    });

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

    public static string Resolution(IReadOnlyList<Guid> challengeIds)
    {
        var decisions = new JsonArray();
        foreach (var id in challengeIds)
        {
            decisions.Add(new JsonObject
            {
                ["challengeMessageId"] = id.ToString(),
                ["summary"] = "Accept the challenge.",
                ["resolution"] = "accepted",
                ["rationale"] = "A lookup table would duplicate the real calculation.",
                ["resultingPlanChanges"] = "The scope moves to the Feature file.",
                ["nextAction"] = "Implement the revised plan.",
            });
        }

        return Serialize(new JsonObject
        {
            ["summary"] = "The challenge is accepted and the plan is narrowed.",
            ["decisions"] = decisions,
            ["revisedProposal"] = new JsonObject
            {
                ["summary"] = "Implement the total in the Feature file.",
                ["scope"] = "Make Total in src/Feature.cs add its two operands and touch no other file.",
                ["implementationSteps"] = ManifestInfo.RevisedPlanMarker + ": edit src/Feature.cs so Total returns the sum of left and right.",
                ["risks"] = "A wrong edit changes every total.",
                ["verificationPlan"] = "Run the configured verification command against the edited file.",
                ["escalationPoints"] = "None expected.",
            },
        });
    }

    public static string ImplementationReport() => Serialize(new JsonObject
    {
        ["summary"] = "Implemented Total in the Feature file.",
        ["changedRelativePaths"] = new JsonArray(OwnedLocation.CandidateRelativePath),
        ["implementationNotes"] = "Implemented the revised plan in the single named file.",
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

    public static string ReviewApproved() => Serialize(new JsonObject
    {
        ["outcome"] = "approved",
        ["summary"] = "The implementation matches the revised plan.",
        ["rationale"] = "Total adds its operands and the local verification passed.",
        ["residualRisks"] = "None material.",
        ["findings"] = new JsonArray(),
    });

    private static string Serialize(JsonObject value) => value.ToJsonString(new JsonSerializerOptions());
}
