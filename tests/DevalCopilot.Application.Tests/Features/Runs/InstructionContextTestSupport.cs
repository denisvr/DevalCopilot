using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Instruction-context inputs and assertions for tests that exercise a manifest builder or a claim handler. The
/// expected text, lengths and identities in the tests themselves are literals or are derived here from literal bytes,
/// never taken from the production writer.</summary>
internal static class InstructionContextTestSupport
{
    public static readonly Guid WorkspaceId = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
    public static readonly Guid CheckpointId = Guid.Parse("c1c1c1c1-c1c1-c1c1-c1c1-c1c1c1c1c1c1");
    public static readonly string Fingerprint = new('f', 64);

    /// <summary>The conventions of the fixture project: escaping, CRLF, multibyte and angle-bracket characters.</summary>
    public const string AgentsText = "# Project conventions\r\nName handlers <Operation>Handler & say \"yes\" — naïve 𝄞.\r\n";

    /// <summary>What handler tests that go through <see cref="UntrackedManifestTestSupport.Evidence"/> deliver: a
    /// complete AGENTS.md and an ignored CLAUDE.md.</summary>
    public static readonly GitWorkspaceInstructionContext Delivered = new(
    [
        File("AGENTS.md", AgentsText),
        new GitWorkspaceInstructionFile(
            "CLAUDE.md", GitWorkspaceInstructionStatus.Omitted, GitWorkspaceInstructionOmission.Ignored, null, null, null),
    ]);

    /// <summary>The section a claim carries when its Git capture returned no instruction context at all.</summary>
    public static ProjectInstructionContextManifest NotCaptured => From(null);

    public static ProjectInstructionContextManifest From(GitWorkspaceInstructionContext? captured) =>
        ProjectInstructionContextManifest.Prepare(WorkspaceId, CheckpointId, Fingerprint, captured);

    /// <summary>A context whose two files are exactly the supplied texts (null means proven absent).</summary>
    public static GitWorkspaceInstructionContext Texts(string? agents, string? claude) => new(
    [
        File("AGENTS.md", agents),
        File("CLAUDE.md", claude),
    ]);

    public static GitWorkspaceInstructionFile File(string name, string? text) => text is null
        ? new GitWorkspaceInstructionFile(name, GitWorkspaceInstructionStatus.Absent, null, null, null, null)
        : new GitWorkspaceInstructionFile(name, GitWorkspaceInstructionStatus.Complete, null, ByteCount(text), Sha256(text), text);

    public static long ByteCount(string text) => Encoding.UTF8.GetByteCount(text);

    public static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>Asserts the sealed manifest delivers <see cref="Delivered"/> exactly, bound to the claim's own workspace and
    /// checkpoint, after a fixed boundary, with no legacy fixed instruction references.</summary>
    public static void AssertManifestCarriesDeliveredInstructions(JsonElement root)
    {
        Assert.False(root.TryGetProperty("instructionReferences", out _));
        var members = root.EnumerateObject().Select(property => property.Name).ToArray();
        var section = Array.IndexOf(members, "projectInstructionContext");
        Assert.True(section > 0);
        Assert.Equal("projectInstructionContextBoundary", members[section - 1]);
        Assert.Equal(ProjectInstructionContextManifest.Boundary, root.GetProperty("projectInstructionContextBoundary").GetString());

        var context = root.GetProperty("projectInstructionContext");
        Assert.Equal(1, context.GetProperty("version").GetInt32());
        Assert.Equal(First(root, "gitWorkspaceId", "workspaceId"), context.GetProperty("sourceGitWorkspaceId").GetString());
        Assert.Equal(First(root, "gitCheckpointId", "resultGitCheckpointId", "startingCheckpointId"), context.GetProperty("sourceGitCheckpointId").GetString());
        Assert.Equal(
            First(root, "checkpointFingerprintSha256", "resultCheckpointFingerprintSha256", "startingFingerprint"),
            context.GetProperty("sourceCheckpointFingerprintSha256").GetString());

        var sources = context.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(2, sources.Length);
        Assert.Equal("AGENTS.md", sources[0].GetProperty("fileName").GetString());
        Assert.Equal("Complete", sources[0].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, sources[0].GetProperty("reason").ValueKind);
        Assert.Equal(ByteCount(AgentsText), sources[0].GetProperty("byteLength").GetInt64());
        Assert.Equal(Sha256(AgentsText), sources[0].GetProperty("sha256").GetString());
        Assert.Equal(AgentsText, sources[0].GetProperty("text").GetString());
        Assert.Equal("CLAUDE.md", sources[1].GetProperty("fileName").GetString());
        Assert.Equal("Omitted", sources[1].GetProperty("status").GetString());
        Assert.Equal("ignored", sources[1].GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, sources[1].GetProperty("text").ValueKind);
        Assert.Equal(JsonValueKind.Null, sources[1].GetProperty("byteLength").ValueKind);
        Assert.Equal(JsonValueKind.Null, sources[1].GetProperty("sha256").ValueKind);
        Assert.True(Encoding.UTF8.GetByteCount(context.GetRawText()) <= 12 * 1024);

        // The repository text sits only inside the section, never in any other member.
        foreach (var property in root.EnumerateObject().Where(property => property.Name != "projectInstructionContext"))
        {
            Assert.DoesNotContain("Name handlers", property.Value.GetRawText(), StringComparison.Ordinal);
        }
    }

    private static string? First(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value))
            {
                return value.GetString();
            }
        }

        throw new InvalidOperationException("The manifest names none of " + string.Join(", ", names));
    }
}
