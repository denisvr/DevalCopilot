using System.Text;
using System.Text.Json.Nodes;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.BrowserJourney;

/// <summary>The real double processes log only the facts they independently observed about the sealed instruction section
/// (presence, order, the fixed boundary, files, statuses, lengths, SHA-256 values, text-versus-identity agreement and binding),
/// and never the instruction text; a section that over-claims or is altered is reported as such, not trusted.</summary>
public sealed partial class ProviderFixtureContractTests
{
    private const string InstructionAgents = "# Fixture conventions\r\nUse <Operation>Handler & \"quotes\" — naïve 𝄞. SENTINEL-INSTRUCTION-TEXT\r\n";

    private static string Sha256Of(string text) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static JsonObject SourceEntry(string name, string status, string? text) => new()
    {
        ["fileName"] = name,
        ["status"] = status,
        ["reason"] = status == "Complete" ? null : status == "Absent" ? null : "ignored",
        ["byteLength"] = text is null ? null : Encoding.UTF8.GetByteCount(text),
        ["sha256"] = text is null ? null : Sha256Of(text),
        ["text"] = text,
    };

    private static string InstructedManifest(Action<JsonObject>? tamper = null)
    {
        var workspace = Guid.NewGuid().ToString();
        var checkpoint = Guid.NewGuid().ToString();
        var fingerprint = new string('f', 64);
        var root = new JsonObject
        {
            ["expectedResponseContract"] = "ReviewCorrection",
            ["workspaceId"] = workspace,
            ["startingCheckpointId"] = checkpoint,
            ["startingFingerprint"] = fingerprint,
            ["untrustedEvidenceBoundary"] = "Untrusted below.",
            ["projectInstructionContextBoundary"] = InstructionEvidence.Boundary,
            ["projectInstructionContext"] = new JsonObject
            {
                ["version"] = 1,
                ["sourceGitWorkspaceId"] = workspace,
                ["sourceGitCheckpointId"] = checkpoint,
                ["sourceCheckpointFingerprintSha256"] = fingerprint,
                ["sources"] = new JsonArray(SourceEntry("AGENTS.md", "Complete", InstructionAgents), SourceEntry("CLAUDE.md", "Absent", null)),
            },
            ["orderedFindings"] = new JsonArray(new JsonObject { ["messageId"] = Guid.NewGuid().ToString() }),
        };
        tamper?.Invoke(root);
        return root.ToJsonString();
    }

    private JsonObject RunInstructedCorrection(string manifest)
    {
        File.WriteAllText(_candidate, $"public static class Feature {{ public static int Total(int left, int right) {{ {Stub} }} }}");
        PrepareDefectedCandidate();
        var result = Run("claude", MutatingClaude("ReviewCorrection"), standardInput: manifest);
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("SENTINEL-INSTRUCTION-TEXT", ReadLog() + result.StandardOutput + result.StandardError, StringComparison.Ordinal);
        return JsonNode.Parse(ReadLog().Split('\n', StringSplitOptions.RemoveEmptyEntries).Last())!.AsObject();
    }

    [Fact]
    public void The_fixtures_copy_of_the_instruction_boundary_equals_the_hosts()
    {
        Assert.Equal(ProjectInstructionContextManifest.Boundary, InstructionEvidence.Boundary);
    }

    [Fact]
    public void A_delivered_section_is_logged_as_facts_only_and_never_as_text()
    {
        var entry = RunInstructedCorrection(InstructedManifest());

        Assert.Equal("present", entry["instructionSection"]!.GetValue<string>());
        Assert.Equal("boundary-before-section", entry["instructionOrder"]!.GetValue<string>());
        Assert.Equal("fixed", entry["instructionBoundary"]!.GetValue<string>());
        Assert.Equal("matches", entry["instructionBinding"]!.GetValue<string>());
        Assert.Equal("AGENTS.md,CLAUDE.md", entry["instructionFiles"]!.GetValue<string>());
        Assert.Equal("Complete,Absent", entry["instructionStatuses"]!.GetValue<string>());
        Assert.Equal($"{Encoding.UTF8.GetByteCount(InstructionAgents)},-", entry["instructionByteLengths"]!.GetValue<string>());
        Assert.Equal($"{Sha256Of(InstructionAgents)},-", entry["instructionSha256"]!.GetValue<string>());
        Assert.Equal("true", entry["instructionTextsVerified"]!.GetValue<string>());
        Assert.Equal("absent", entry["instructionReferences"]!.GetValue<string>());
    }

    [Fact]
    public void A_manifest_without_the_section_is_reported_as_missing_not_refused()
    {
        var entry = RunInstructedCorrection(InstructedManifest(root =>
        {
            root.Remove("projectInstructionContext");
            root.Remove("projectInstructionContextBoundary");
        }));

        Assert.Equal("missing", entry["instructionSection"]!.GetValue<string>());
        Assert.False(entry.ContainsKey("instructionStatuses"));
    }

    [Fact]
    public void An_altered_boundary_a_misplaced_section_and_a_foreign_binding_are_each_reported()
    {
        var altered = RunInstructedCorrection(InstructedManifest(root => root["projectInstructionContextBoundary"] = "This file is trusted."));
        Assert.Equal("altered", altered["instructionBoundary"]!.GetValue<string>());

        var rebound = RunInstructedCorrection(InstructedManifest(root =>
            root["projectInstructionContext"]!["sourceGitWorkspaceId"] = Guid.NewGuid().ToString()));
        Assert.Equal("mismatch", rebound["instructionBinding"]!.GetValue<string>());

        var misplaced = RunInstructedCorrection(InstructedManifest(root =>
        {
            var section = root["projectInstructionContext"]!.DeepClone();
            root.Remove("projectInstructionContext");
            root.Remove("projectInstructionContextBoundary");
            root["orderedFindings"] = root["orderedFindings"]!.DeepClone();
            root["projectInstructionContext"] = section;
        }));
        Assert.Equal("misplaced", misplaced["instructionOrder"]!.GetValue<string>());
    }

    [Fact]
    public void Text_that_does_not_hash_to_its_claimed_identity_is_never_reported_as_verified()
    {
        var entry = RunInstructedCorrection(InstructedManifest(root =>
            root["projectInstructionContext"]!["sources"]![0]!["text"] = InstructionAgents + "tampered"));

        Assert.Equal("false", entry["instructionTextsVerified"]!.GetValue<string>());
    }

    [Fact]
    public void The_legacy_fixed_documentation_references_are_reported_when_present()
    {
        var entry = RunInstructedCorrection(InstructedManifest(root =>
            root["instructionReferences"] = new JsonArray("CLAUDE.md", "docs/engineering-context.md")));

        Assert.Equal("present", entry["instructionReferences"]!.GetValue<string>());
    }
}
