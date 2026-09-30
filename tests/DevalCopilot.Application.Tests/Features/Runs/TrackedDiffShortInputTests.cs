using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.TrackedDiffFixture;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>A short diff that is not a fully supported text or metadata-only patch must never be inlined as an
/// exact string marked complete; it gets truthful, bounded omission metadata instead.</summary>
public sealed class TrackedDiffShortInputTests
{
    private static readonly IReadOnlyList<GitWorkspaceChangedPath> Paths = [new("f.txt", null, " ", "M")];

    public static IEnumerable<object[]> Cases()
    {
        foreach (var variant in new[]
        {
            "critical-review", "challenge-resolution", "implementation-accepted", "implementation-revised",
            "implementation-review", "implementation-review-correction", "review-correction",
        })
        {
            yield return [variant, "short non-header string", "diff", "unsupported_format", null!];
            yield return [variant, "malformed hunk", "diff --git a/f.txt b/f.txt\nindex 1..2 100644\n--- a/f.txt\n+++ b/f.txt\n@@ -1,3 +1,3 @@\n a\n-b\n+c\n", null!, "malformed_hunk"];
            yield return [variant, "unsupported header", "diff --git a/f.txt b/f.txt\nsurprise 1\n--- a/f.txt\n+++ b/f.txt\n@@ -1 +1 @@\n-a\n+b\n", null!, "unsupported_format"];
            yield return [variant, "unparseable path", "diff --git a/f.txt b/g.txt\nindex 1..2 100644\n--- a/f.txt\n+++ b/g.txt\n@@ -1 +1 @@\n-a\n+b\n", null!, "header_unparseable"];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_short_unsupported_diff_is_never_inlined_as_an_exact_complete_string(
        string variant, string name, string diff, string? wholeReason, string? itemReason)
    {
        Assert.NotEmpty(name);

        var manifest = UntrackedFileManifestTests.Builder(variant)(Paths, diff, null, 0);

        using var document = JsonDocument.Parse(manifest);
        var evidence = document.RootElement.GetProperty("changeEvidence");
        Assert.True(evidence.GetProperty("diffTruncated").GetBoolean());
        Assert.NotEqual(diff, evidence.GetProperty("diff").ValueKind == JsonValueKind.Null ? null : evidence.GetProperty("diff").GetString());
        var selection = evidence.GetProperty("diffSelection");
        Assert.False(selection.GetProperty("complete").GetBoolean());
        if (wholeReason is not null)
        {
            Assert.Equal(wholeReason, selection.GetProperty("reason").GetString());
            Assert.Equal(JsonValueKind.Null, evidence.GetProperty("diff").ValueKind);
        }
        else
        {
            var item = Assert.Single(selection.GetProperty("items").EnumerateArray());
            Assert.Equal(itemReason, item.GetProperty("reason").GetString());
            Assert.Equal("omitted", item.GetProperty("selection").GetString());
            Assert.Equal(1, selection.GetProperty("files").GetProperty("omitted").GetInt32());
        }
    }

    [Theory]
    [MemberData(nameof(UntrackedFileManifestTests.Variants), MemberType = typeof(UntrackedFileManifestTests))]
    public void A_valid_small_text_and_metadata_only_diff_keeps_its_exact_complete_string(string variant)
    {
        var diff = TextFile("f.txt", Hunk(1, "-a", "+b")) + ModeChange("run.sh");

        using var document = JsonDocument.Parse(UntrackedFileManifestTests.Builder(variant)(Paths, diff, null, 0));
        var evidence = document.RootElement.GetProperty("changeEvidence");

        Assert.Equal(diff, evidence.GetProperty("diff").GetString());
        Assert.False(evidence.GetProperty("diffTruncated").GetBoolean());
        Assert.False(evidence.TryGetProperty("diffSelection", out _));
    }
}
