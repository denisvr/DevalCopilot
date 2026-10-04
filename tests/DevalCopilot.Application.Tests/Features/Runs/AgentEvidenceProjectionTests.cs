using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The delivery projection of ADR-0021 and ADR-0024: the two root instruction names are reserved to the instruction
/// section, so their generic untracked preview and tracked evidence (comparison text, hunks and samples) never reach an Agent manifest,
/// whatever a reader's facts claim, at every reduction step and in every builder form, while unrelated evidence is untouched and the
/// withholding is always stated. The projection also removes the raw working-path patch from the delivered capture.</summary>
public sealed class AgentEvidenceProjectionTests
{
    private const string Secret = "RESERVED-ROOT-TEXT-5d1e";

    private static GitWorkspaceChangedPath Untracked(string path) => new(path, null, "?", "?");

    private static GitWorkspaceTrackedFile Edit(string path, string before, string after) => new(path, null, before, after);

    private static GitWorkspaceTrackedFile Root(string path) => Edit(path, "old " + path + "\n", Secret + " new " + path + "\n");

    private static GitWorkspaceTrackedFile Other(string path = "src/Other.cs") => Edit(path, "old other\n", "OTHER-CHANGE-LINE new other\n");

    private static string Build(
        string variant, IReadOnlyList<GitWorkspaceChangedPath> paths, IReadOnlyList<GitWorkspaceTrackedFile> facts,
        IReadOnlyList<GitWorkspaceUntrackedFile> untracked, int padding) =>
        UntrackedFileManifestTests.AttestedBuilder(variant)(paths, TrackedChangeEvidence.Derive(paths, facts), untracked, padding);

    // ---- the capture projection --------------------------------------------------------------------------------------------------

    [Fact]
    public void The_projection_removes_the_raw_patch_and_keeps_the_fingerprint_paths_facts_and_untracked_evidence()
    {
        var facts = new[] { Other() };
        var result = new GitWorkspaceEvidenceResult(
            GitWorkspaceEvidenceOutcome.Success, new string('a', 40), new string('b', 64),
            [TrackedFixture.Modified("src/Other.cs")],
            "diff --git a/src/Other.cs b/src/Other.cs\n+raw patch text read through the repository pathname\n",
            [new GitWorkspaceUntrackedFile("notes/n.txt", null, 5, "kept!", true)],
            InstructionContext: null,
            TrackedFiles: facts);

        var once = AgentEvidenceProjection.Project(result);

        Assert.Null(once.CompleteDiff);
        Assert.Equal(result.FingerprintSha256, once.FingerprintSha256);
        Assert.Equal(result.HeadCommitSha, once.HeadCommitSha);
        Assert.Equal(result.ChangedPaths, once.ChangedPaths);
        Assert.Equal(facts, once.TrackedFiles);
        Assert.Equal("kept!", Assert.Single(once.UntrackedFiles!).Text);
        Assert.Null(AgentEvidenceProjection.Project(once).CompleteDiff);
    }

    [Fact]
    public void The_projection_replaces_reserved_untracked_entries_keeps_everything_else_and_is_idempotent()
    {
        var result = new GitWorkspaceEvidenceResult(
            GitWorkspaceEvidenceOutcome.Success, new string('a', 40), new string('b', 64),
            [Untracked("AGENTS.md"), Untracked("notes/n.txt"), TrackedFixture.Modified("CLAUDE.md"), Untracked("zz.txt")],
            "raw",
            [
                new GitWorkspaceUntrackedFile("AGENTS.md", null, 30, Secret, true),
                new GitWorkspaceUntrackedFile("notes/n.txt", null, 5, "kept!", true),
                new GitWorkspaceUntrackedFile("zz.txt", GitWorkspaceUntrackedOmission.Binary, 3, null, false),
            ],
            InstructionContext: null);

        var once = AgentEvidenceProjection.Project(result);
        var twice = AgentEvidenceProjection.Project(once);

        Assert.Equal(["AGENTS.md", "notes/n.txt", "zz.txt"], once.UntrackedFiles!.Select(file => file.Path));
        Assert.Equal(GitWorkspaceUntrackedOmission.ReservedInstructionFile, once.UntrackedFiles![0].Omission);
        Assert.Null(once.UntrackedFiles[0].Text);
        Assert.False(once.UntrackedFiles[0].ContentComplete);
        Assert.Equal("kept!", once.UntrackedFiles[1].Text);
        Assert.Equal(result.FingerprintSha256, once.FingerprintSha256);
        Assert.Equal(result.ChangedPaths, once.ChangedPaths);
        Assert.Equal(once.UntrackedFiles, twice.UntrackedFiles);
    }

    [Fact]
    public void A_reserved_untracked_path_with_no_captured_entry_is_stated_and_a_capture_without_previews_stays_without_them()
    {
        var withPreviews = new GitWorkspaceEvidenceResult(
            GitWorkspaceEvidenceOutcome.Success, null, null, [Untracked("CLAUDE.md")], null, []);
        var without = withPreviews with { UntrackedFiles = null };

        Assert.Equal(
            GitWorkspaceUntrackedOmission.ReservedInstructionFile,
            Assert.Single(AgentEvidenceProjection.Project(withPreviews).UntrackedFiles!).Omission);
        Assert.Null(AgentEvidenceProjection.Project(without).UntrackedFiles);
    }

    // ---- the manifest: every builder variant, every reduction step ---------------------------------------------------------------

    private static (GitWorkspaceChangedPath[] Paths, GitWorkspaceTrackedFile[] Facts, GitWorkspaceUntrackedFile[] Untracked) MixedEvidence() =>
    (
        [TrackedFixture.Modified("AGENTS.md"), Untracked("CLAUDE.md"), TrackedFixture.Modified("src/Other.cs"), Untracked("notes/new.txt")],
        [Root("AGENTS.md"), Other()],
        [
            // A capture that did NOT project (another reader): the builder still never delivers the reserved text.
            new GitWorkspaceUntrackedFile("CLAUDE.md", null, 40, Secret + " untracked claude", true),
            new GitWorkspaceUntrackedFile("notes/new.txt", null, 20, "NOTES-KEPT", true),
        ]
    );

    [Theory]
    [MemberData(nameof(UntrackedFileManifestTests.Variants), MemberType = typeof(UntrackedFileManifestTests))]
    public void No_builder_variant_delivers_reserved_text_even_when_the_facts_claim_it_and_each_states_the_withholding(string variant)
    {
        var (paths, facts, untracked) = MixedEvidence();

        var manifest = Build(variant, paths, facts, untracked, 100);

        Assert.DoesNotContain(Secret, manifest, StringComparison.Ordinal);
        Assert.Contains("OTHER-CHANGE-LINE", manifest, StringComparison.Ordinal);
        Assert.Contains("NOTES-KEPT", manifest, StringComparison.Ordinal);
        AssertStated(manifest);
    }

    [Theory]
    [MemberData(nameof(UntrackedFileManifestTests.Variants), MemberType = typeof(UntrackedFileManifestTests))]
    public void The_withholding_survives_every_reduction_step_and_the_ceiling_fit_for_every_variant(string variant)
    {
        var (paths, facts, untracked) = MixedEvidence();

        for (var padding = 2_000; padding <= 30_000; padding += 750)
        {
            var manifest = Build(variant, paths, facts, untracked, padding);

            Assert.DoesNotContain(Secret, manifest, StringComparison.Ordinal);
            AssertStated(manifest);
            using var document = JsonDocument.Parse(manifest);
            Assert.False(document.RootElement.GetProperty("changeEvidence").GetProperty("diffSelection").GetProperty("complete").GetBoolean());
        }
    }

    [Theory]
    [MemberData(nameof(UntrackedFileManifestTests.Variants), MemberType = typeof(UntrackedFileManifestTests))]
    public void A_capture_with_no_reserved_change_delivers_the_attested_comparison_exactly(string variant)
    {
        var paths = new[] { TrackedFixture.Modified("src/Other.cs"), Untracked("notes/new.txt") };
        var untracked = new[] { new GitWorkspaceUntrackedFile("notes/new.txt", null, 20, "NOTES-KEPT", true) };

        var manifest = Build(variant, paths, [Other()], untracked, 100);

        using var document = JsonDocument.Parse(manifest);
        var evidence = document.RootElement.GetProperty("changeEvidence");
        Assert.False(evidence.GetProperty("diffTruncated").GetBoolean());
        Assert.False(evidence.TryGetProperty("diffSelection", out _));
        Assert.Equal(
            "diff --git a/src/Other.cs b/src/Other.cs\n--- a/src/Other.cs\n+++ b/src/Other.cs\n@@ -1,1 +1,1 @@\n-old other\n+OTHER-CHANGE-LINE new other\n",
            evidence.GetProperty("diff").GetString());
    }

    [Fact]
    public void A_reserved_change_with_no_facts_at_all_still_states_the_withholding_and_is_never_complete()
    {
        var paths = new[] { TrackedFixture.Modified("CLAUDE.md") };

        var manifest = Build("critical-review", paths, [], [], 100);

        using var document = JsonDocument.Parse(manifest);
        var evidence = document.RootElement.GetProperty("changeEvidence");
        Assert.True(evidence.GetProperty("diffTruncated").GetBoolean());
        Assert.Equal(string.Empty, evidence.GetProperty("diff").GetString());
        var selection = evidence.GetProperty("diffSelection");
        Assert.Equal(["CLAUDE.md"], selection.GetProperty("reservedInstructionFiles").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(1, selection.GetProperty("omissionReasons").GetProperty("reserved_instruction_file").GetInt32());
    }

    [Fact]
    public void Samples_of_an_oversized_reserved_comparison_are_never_delivered()
    {
        var huge = Edit("AGENTS.md", Lines("old", 400), Lines(Secret, 400));
        var paths = new[] { TrackedFixture.Modified("AGENTS.md"), TrackedFixture.Modified("src/Other.cs") };

        var manifest = Build("critical-review", paths, [huge, Other()], [], 100);

        Assert.DoesNotContain(Secret, manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("\"samples\"", manifest, StringComparison.Ordinal);
        Assert.Contains("OTHER-CHANGE-LINE", manifest, StringComparison.Ordinal);
    }

    private static string Lines(string prefix, int count) =>
        string.Concat(Enumerable.Range(0, count).Select(index => $"{prefix} line {index}\n"));

    /// <summary>Both reserved paths stay in <c>changedPaths</c>, the tracked one is named under the diff selection and counted as an
    /// omission with the fixed reason, the untracked one is omitted with the fixed reason (or the whole untracked section is its
    /// counts-only form), and nothing is called complete.</summary>
    private static void AssertStated(string manifest)
    {
        using var document = JsonDocument.Parse(manifest);
        var evidence = document.RootElement.GetProperty("changeEvidence");
        var changed = evidence.GetProperty("changedPaths").EnumerateArray().Select(path => path.GetProperty("Path").GetString()).ToArray();
        Assert.Contains("AGENTS.md", changed);
        Assert.Contains("CLAUDE.md", changed);
        Assert.True(evidence.GetProperty("diffTruncated").GetBoolean());
        var selection = evidence.GetProperty("diffSelection");
        var reserved = selection.GetProperty("reservedInstructionFiles").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(["AGENTS.md", "CLAUDE.md"], reserved);
        Assert.Equal(1, selection.GetProperty("omissionReasons").GetProperty("reserved_instruction_file").GetInt32());
        var untracked = evidence.GetProperty("untrackedFiles");
        Assert.False(untracked.GetProperty("allFilesComplete").GetBoolean());
        if (untracked.GetProperty("files").GetArrayLength() > 0)
        {
            var claude = untracked.GetProperty("files").EnumerateArray().Single(file => file.GetProperty("path").GetString() == "CLAUDE.md");
            Assert.Equal("reserved_instruction_file", claude.GetProperty("omissionReason").GetString());
            Assert.Equal("omitted", claude.GetProperty("preview").GetString());
            Assert.False(claude.GetProperty("contentComplete").GetBoolean());
            Assert.Equal(JsonValueKind.Null, claude.GetProperty("text").ValueKind);
        }
        else
        {
            Assert.Equal("manifest_budget", untracked.GetProperty("omissionReason").GetString());
        }

        Assert.True(Encoding.UTF8.GetByteCount(manifest) > 0);
    }
}
