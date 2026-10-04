using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Shared evidence and assertions for handler tests that prove untracked-file previews reach
/// the sealed Agent manifest exactly, under the untrusted boundary.</summary>
internal static class UntrackedManifestTestSupport
{
    public const string PreviewText = "handler preview text\nsecond line";

    public static GitWorkspaceEvidenceResult Evidence(string fingerprintSha256) => new(
        GitWorkspaceEvidenceOutcome.Success,
        new string('a', 40),
        fingerprintSha256,
        [
            new GitWorkspaceChangedPath("notes/new.txt", null, "?", "?"),
            new GitWorkspaceChangedPath("blob.bin", null, "?", "?"),
        ],
        null,
        [
            new GitWorkspaceUntrackedFile("notes/new.txt", null, 31, PreviewText, true),
            new GitWorkspaceUntrackedFile("blob.bin", GitWorkspaceUntrackedOmission.Binary, 12, null, false),
        ],
        InstructionContextTestSupport.Delivered);

    public static void AssertManifestCarriesPreviews(string manifestJson)
    {
        using var document = JsonDocument.Parse(manifestJson);
        AssertManifestCarriesPreviews(document.RootElement);
    }

    public static void AssertManifestCarriesPreviews(JsonElement root)
    {
        var files = root.GetProperty("changeEvidence").GetProperty("untrackedFiles").GetProperty("files")
            .EnumerateArray().ToArray();
        Assert.Equal(["blob.bin", "notes/new.txt"], files.Select(file => file.GetProperty("path").GetString()));
        Assert.Equal("binary", files[0].GetProperty("omissionReason").GetString());
        Assert.Equal(JsonValueKind.Null, files[0].GetProperty("text").ValueKind);
        Assert.Equal(PreviewText, files[1].GetProperty("text").GetString());
        Assert.True(files[1].GetProperty("contentComplete").GetBoolean());
        var boundary = root.GetProperty("untrustedEvidenceBoundary").GetString()!;
        Assert.True(boundary.Contains("changeEvidence", StringComparison.Ordinal)
            || boundary.Contains("change evidence", StringComparison.Ordinal));
        foreach (var property in root.EnumerateObject().Where(p => p.Name != "changeEvidence"))
        {
            Assert.DoesNotContain("handler preview text", property.Value.GetRawText(), StringComparison.Ordinal);
        }

        InstructionContextTestSupport.AssertManifestCarriesDeliveredInstructions(root);
    }
}
