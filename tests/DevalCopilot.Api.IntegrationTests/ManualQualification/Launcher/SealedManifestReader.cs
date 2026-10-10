using System.Text.Json;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

public static class SealedManifestReader
{
    /// <summary>Reads three named members of a host-sealed context manifest. The planner manifest declares its contract as
    /// <c>expectedMessageType</c>; the review manifest as <c>expectedResponseContract</c> and names the reviewed proposal.</summary>
    public static SealedManifestFacts Read(byte[] manifest, string expectedContract, string expectedObjective)
    {
        try
        {
            using var document = JsonDocument.Parse(manifest);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return SealedManifestFacts.None;
            }

            var contractMember = expectedContract == "Proposal" ? "expectedMessageType" : "expectedResponseContract";
            var contractAgrees = root.TryGetProperty(contractMember, out var declared)
                && declared.ValueKind == JsonValueKind.String && declared.GetString() == expectedContract;
            var objectiveAgrees = root.TryGetProperty("objective", out var recorded)
                && recorded.ValueKind == JsonValueKind.String && recorded.GetString() == expectedObjective;
            Guid? proposal = root.TryGetProperty("reviewedProposal", out var reviewed)
                && reviewed.ValueKind == JsonValueKind.Object
                && reviewed.TryGetProperty("messageId", out var messageId)
                && messageId.TryGetGuid(out var parsed)
                ? parsed
                : null;
            return new SealedManifestFacts(contractAgrees, objectiveAgrees, proposal);
        }
        catch (JsonException)
        {
            return SealedManifestFacts.None;
        }
    }
}
