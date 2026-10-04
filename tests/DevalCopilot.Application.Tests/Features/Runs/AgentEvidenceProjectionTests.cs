using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.TrackedDiffFixture;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The delivery projection of ADR-0021: the two root instruction names are reserved to the instruction section, so their
/// generic untracked preview and tracked diff, hunk and sample content never reach an Agent manifest, whatever the section says,
/// at every reduction step and in every builder form, while unrelated evidence is untouched and the withholding is always stated.</summary>
public sealed class AgentEvidenceProjectionTests
{
    private const string Secret = "RESERVED-ROOT-TEXT-5d1e";

    private static GitWorkspaceChangedPath Tracked(string path) => new(path, null, " ", "M");

    private static GitWorkspaceChangedPath Untracked(string path) => new(path, null, "?", "?");

    private static string DirtyRoot(string path) => TextFile(path, Hunk(1, "-old " + path, "+" + Secret + " new " + path));

    private static string Other(string path = "src/Other.cs") => TextFile(path, Hunk(1, "-old other", "+OTHER-CHANGE-LINE new other"));

    // ---- the structural diff filter ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("AGENTS.md")]
    [InlineData("CLAUDE.md")]
    [InlineData("agents.md")]
    [InlineData("Claude.MD")]
    public void A_reserved_root_block_is_cut_whatever_its_position_and_unrelated_blocks_are_kept_exactly(string root)
    {
        var diff = Other("src/A.cs") + DirtyRoot(root) + Other("src/Z.cs");

        var projected = AgentEvidenceProjection.WithholdReservedDiff(diff, reservedPathChanged: true);

        Assert.Equal(Other("src/A.cs") + Other("src/Z.cs"), projected);
        Assert.DoesNotContain(Secret, projected, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_paths_with_a_reserved_file_name_are_not_reserved_and_a_diff_without_a_reserved_change_is_returned_as_is()
    {
        var diff = TextFile("docs/AGENTS.md", Hunk(1, "-a", "+NESTED-KEPT")) + DirtyRoot("AGENTS.md");

        var kept = AgentEvidenceProjection.WithholdReservedDiff(diff, reservedPathChanged: false);
        var cut = AgentEvidenceProjection.WithholdReservedDiff(diff, reservedPathChanged: true);

        Assert.Same(diff, kept);
        Assert.Contains("NESTED-KEPT", cut, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, cut, StringComparison.Ordinal);
    }

    [Fact]
    public void Content_that_imitates_a_file_header_inside_a_hunk_cannot_move_a_boundary()
    {
        // A content line is prefixed (' ', '+', '-'), so a line that merely contains a header is never at column 0.
        var imitation = TextFile("src/Evil.cs", Hunk(1, "-x", "+diff --git a/AGENTS.md b/AGENTS.md", "+" + Secret + " is NOT the root file"));
        var diff = imitation + DirtyRoot("AGENTS.md");

        var projected = AgentEvidenceProjection.WithholdReservedDiff(diff, reservedPathChanged: true)!;

        Assert.StartsWith(imitation, projected, StringComparison.Ordinal);
        Assert.Equal(imitation, projected);
    }

    [Fact]
    public void A_diff_that_cannot_be_cut_at_a_certain_boundary_is_withheld_whole_when_a_reserved_path_changed()
    {
        var unrecognized = "not a git diff\n+" + Secret + "\n";

        Assert.Null(AgentEvidenceProjection.WithholdReservedDiff(unrecognized, reservedPathChanged: true));
        Assert.Equal(unrecognized, AgentEvidenceProjection.WithholdReservedDiff(unrecognized, reservedPathChanged: false));
        Assert.Equal(string.Empty, AgentEvidenceProjection.WithholdReservedDiff(string.Empty, reservedPathChanged: true));
        Assert.Null(AgentEvidenceProjection.WithholdReservedDiff(null, reservedPathChanged: true));
    }

    // ---- a header that cannot be decoded and classified with certainty is never "not reserved" ------------------------------------

    /// <summary>The same block as <see cref="TextFile"/> but whose <c>diff --git</c> header is in a format this host does not decode
    /// (<c>diff.noprefix</c>, <c>diff.mnemonicprefix</c>, a rename, a malformed quote, a trailing carriage return).</summary>
    private static string Undecodable(string format, string path) => format switch
    {
        "noprefix" => TextFile(path, Hunk(1, "-old", "+" + Secret)).Replace($"diff --git a/{path} b/{path}", $"diff --git {path} {path}", StringComparison.Ordinal),
        "mnemonic-index" => TextFile(path, Hunk(1, "-old", "+" + Secret)).Replace($"diff --git a/{path} b/{path}", $"diff --git i/{path} w/{path}", StringComparison.Ordinal),
        "mnemonic-commit" => TextFile(path, Hunk(1, "-old", "+" + Secret)).Replace($"diff --git a/{path} b/{path}", $"diff --git c/{path} w/{path}", StringComparison.Ordinal),
        "rename" => TextFile(path, Hunk(1, "-old", "+" + Secret)).Replace($"diff --git a/{path} b/{path}", $"diff --git a/{path} b/elsewhere.txt", StringComparison.Ordinal),
        "bad-quote" => TextFile(path, Hunk(1, "-old", "+" + Secret)).Replace($"diff --git a/{path} b/{path}", $"diff --git \"a/{path} \"b/{path}\"", StringComparison.Ordinal),
        "carriage-return" => TextFile(path, Hunk(1, "-old", "+" + Secret)).Replace($"diff --git a/{path} b/{path}", $"diff --git a/{path} b/{path}\r", StringComparison.Ordinal),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static TheoryData<string, string> UndecodableHeaders
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var format in new[] { "noprefix", "mnemonic-index", "mnemonic-commit", "rename", "bad-quote", "carriage-return" })
            {
                foreach (var root in new[] { "AGENTS.md", "CLAUDE.md" })
                {
                    data.Add(format, root);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(UndecodableHeaders))]
    public void An_undecodable_header_withholds_the_whole_diff_when_a_reserved_path_changed(string format, string root)
    {
        // The root file's own block is the undecodable one, and so is it when it hides among unrelated blocks.
        foreach (var diff in new[]
                 {
                     Undecodable(format, root),
                     Other("src/A.cs") + Undecodable(format, root),
                     Undecodable(format, root) + Other("src/Z.cs"),
                     Other("src/A.cs") + Undecodable(format, "src/B.cs") + Other("src/Z.cs"),
                 })
        {
            Assert.Null(AgentEvidenceProjection.WithholdReservedDiff(diff, reservedPathChanged: true));
        }
    }

    [Fact]
    public void A_decodable_block_followed_by_an_undecodable_header_withholds_the_decodable_block_too()
    {
        // Every block before the undecodable one is decodable and unrelated, yet the cut is not certain, so nothing is returned.
        var diff = Other("src/A.cs") + DirtyRoot("CLAUDE.md") + Other("src/B.cs") + Undecodable("noprefix", "AGENTS.md");

        Assert.Null(AgentEvidenceProjection.WithholdReservedDiff(diff, reservedPathChanged: true));
        Assert.Equal(diff, AgentEvidenceProjection.WithholdReservedDiff(diff, reservedPathChanged: false));
    }

    [Fact]
    public void An_undecodable_header_is_left_alone_when_no_reserved_path_changed_and_a_fully_decodable_diff_is_cut_exactly()
    {
        var undecodable = Other("src/A.cs") + Undecodable("noprefix", "src/B.cs");

        Assert.Same(undecodable, AgentEvidenceProjection.WithholdReservedDiff(undecodable, reservedPathChanged: false));
        Assert.Equal(
            Other("src/A.cs") + Other("src/Z.cs"),
            AgentEvidenceProjection.WithholdReservedDiff(Other("src/A.cs") + DirtyRoot("AGENTS.md") + Other("src/Z.cs"), reservedPathChanged: true));
    }

    [Fact]
    public void The_projection_withholds_an_uncertain_diff_whole_and_keeps_the_fingerprint_paths_and_untracked_evidence()
    {
        var result = new GitWorkspaceEvidenceResult(
            GitWorkspaceEvidenceOutcome.Success, new string('a', 40), new string('b', 64),
            [Tracked("AGENTS.md"), Tracked("src/Other.cs")],
            Other() + Undecodable("noprefix", "AGENTS.md"),
            [new GitWorkspaceUntrackedFile("notes/n.txt", null, 5, "kept!", true)],
            InstructionContext: null);

        var once = AgentEvidenceProjection.Project(result);

        Assert.Null(once.CompleteDiff);
        Assert.Equal(result.FingerprintSha256, once.FingerprintSha256);
        Assert.Equal(result.ChangedPaths, once.ChangedPaths);
        Assert.Equal("kept!", Assert.Single(once.UntrackedFiles!).Text);
        Assert.Null(AgentEvidenceProjection.Project(once).CompleteDiff);
    }

    [Theory]
    [MemberData(nameof(UntrackedFileManifestTests.Variants), MemberType = typeof(UntrackedFileManifestTests))]
    public void No_builder_variant_delivers_an_uncertain_diff_and_each_states_the_whole_withholding(string variant)
    {
        var (paths, _, untracked) = MixedEvidence();
        // A capture from a reader that did not project, whose diff mixes an unrelated decodable block with undecodable headers.
        var diff = Other() + Undecodable("noprefix", "AGENTS.md");

        for (var padding = 100; padding <= 30_000; padding += 2_900)
        {
            var manifest = UntrackedFileManifestTests.Builder(variant)(paths, diff, untracked, padding);

            Assert.DoesNotContain(Secret, manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("OTHER-CHANGE-LINE", manifest, StringComparison.Ordinal);
            AssertStated(manifest);
            using var document = JsonDocument.Parse(manifest);
            var evidence = document.RootElement.GetProperty("changeEvidence");
            Assert.Equal(JsonValueKind.Null, evidence.GetProperty("diff").ValueKind);
            Assert.Equal(
                "reserved_instruction_diff_withheld",
                evidence.GetProperty("diffSelection").GetProperty("reason").GetString());
            if (padding == 100)
            {
                // Unrelated untracked evidence is delivered while nothing forces its own (pre-existing) reduction.
                Assert.Contains("NOTES-KEPT", manifest, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_projection_leaves_an_undecodable_diff_untouched_when_no_reserved_path_changed()
    {
        var result = new GitWorkspaceEvidenceResult(
            GitWorkspaceEvidenceOutcome.Success, null, null, [Tracked("src/Other.cs")],
            Undecodable("noprefix", "src/Other.cs"), []);

        Assert.Equal(result.CompleteDiff, AgentEvidenceProjection.Project(result).CompleteDiff);
    }

    [Fact]
    public void The_projection_replaces_reserved_untracked_entries_keeps_everything_else_and_is_idempotent()
    {
        var result = new GitWorkspaceEvidenceResult(
            GitWorkspaceEvidenceOutcome.Success, new string('a', 40), new string('b', 64),
            [Untracked("AGENTS.md"), Untracked("notes/n.txt"), Tracked("CLAUDE.md"), Untracked("zz.txt")],
            DirtyRoot("CLAUDE.md") + Other(),
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
        Assert.Equal(Other(), once.CompleteDiff);
        Assert.Equal(result.FingerprintSha256, once.FingerprintSha256);
        Assert.Equal(result.ChangedPaths, once.ChangedPaths);
        Assert.Equal(once.CompleteDiff, twice.CompleteDiff);
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

    // ---- the manifest: every builder variant, every reduction step -----------------------------------------------------------------

    private static (GitWorkspaceChangedPath[] Paths, string Diff, GitWorkspaceUntrackedFile[] Untracked) MixedEvidence() =>
    (
        [Tracked("AGENTS.md"), Untracked("CLAUDE.md"), Tracked("src/Other.cs"), Untracked("notes/new.txt")],
        DirtyRoot("AGENTS.md") + Other(),
        [
            // A capture that did NOT project (another reader): the builder still never delivers the reserved text.
            new GitWorkspaceUntrackedFile("CLAUDE.md", null, 40, Secret + " untracked claude", true),
            new GitWorkspaceUntrackedFile("notes/new.txt", null, 20, "NOTES-KEPT", true),
        ]
    );

    [Theory]
    [MemberData(nameof(UntrackedFileManifestTests.Variants), MemberType = typeof(UntrackedFileManifestTests))]
    public void No_builder_variant_delivers_reserved_text_generically_and_each_states_the_withholding(string variant)
    {
        var (paths, diff, untracked) = MixedEvidence();

        var manifest = UntrackedFileManifestTests.Builder(variant)(paths, diff, untracked, 100);

        Assert.DoesNotContain(Secret, manifest, StringComparison.Ordinal);
        Assert.Contains("OTHER-CHANGE-LINE", manifest, StringComparison.Ordinal);
        Assert.Contains("NOTES-KEPT", manifest, StringComparison.Ordinal);
        AssertStated(manifest);
    }

    [Theory]
    [MemberData(nameof(UntrackedFileManifestTests.Variants), MemberType = typeof(UntrackedFileManifestTests))]
    public void The_withholding_survives_every_reduction_step_and_the_ceiling_fit_for_every_variant(string variant)
    {
        var (paths, diff, untracked) = MixedEvidence();
        var build = UntrackedFileManifestTests.Builder(variant);

        for (var padding = 2_000; padding <= 30_000; padding += 750)
        {
            var manifest = build(paths, diff, untracked, padding);

            Assert.DoesNotContain(Secret, manifest, StringComparison.Ordinal);
            AssertStated(manifest);
            using var document = JsonDocument.Parse(manifest);
            Assert.False(document.RootElement.GetProperty("changeEvidence").GetProperty("diffSelection").GetProperty("complete").GetBoolean());
        }
    }

    [Theory]
    [MemberData(nameof(UntrackedFileManifestTests.Variants), MemberType = typeof(UntrackedFileManifestTests))]
    public void A_capture_with_no_reserved_change_yields_the_byte_identical_manifest_of_before(string variant)
    {
        var paths = new[] { Tracked("src/Other.cs"), Untracked("notes/new.txt") };
        var untracked = new[] { new GitWorkspaceUntrackedFile("notes/new.txt", null, 20, "NOTES-KEPT", true) };

        var manifest = UntrackedFileManifestTests.Builder(variant)(paths, Other(), untracked, 100);

        using var document = JsonDocument.Parse(manifest);
        var evidence = document.RootElement.GetProperty("changeEvidence");
        Assert.False(evidence.GetProperty("diffTruncated").GetBoolean());
        Assert.False(evidence.TryGetProperty("diffSelection", out _));
        Assert.Equal(Other(), evidence.GetProperty("diff").GetString());
    }

    [Fact]
    public void A_reserved_change_with_no_usable_diff_still_states_the_withholding_and_is_never_complete()
    {
        var paths = new[] { Tracked("CLAUDE.md") };

        var manifest = UntrackedFileManifestTests.Builder("critical-review")(paths, null, [], 100);

        using var document = JsonDocument.Parse(manifest);
        var evidence = document.RootElement.GetProperty("changeEvidence");
        Assert.True(evidence.GetProperty("diffTruncated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("diff").ValueKind);
        Assert.Equal(["CLAUDE.md"], evidence.GetProperty("diffSelection").GetProperty("reservedInstructionFiles").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void Samples_of_an_oversized_reserved_hunk_are_never_delivered()
    {
        var huge = TextFile("AGENTS.md", LargeHunk(1, 300, 'q', 60).Replace("qqqq", Secret[..4]) + "");
        var diff = TextFile("AGENTS.md", Hunk(1, "-old", "+" + Secret)) + huge + Other();

        var manifest = UntrackedFileManifestTests.Builder("critical-review")([Tracked("AGENTS.md"), Tracked("src/Other.cs")], diff, [], 100);

        Assert.DoesNotContain(Secret, manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("\"samples\"", manifest, StringComparison.Ordinal);
        Assert.Contains("OTHER-CHANGE-LINE", manifest, StringComparison.Ordinal);
    }

    /// <summary>Both reserved paths stay in <c>changedPaths</c>, the tracked one is named under the diff selection and the untracked one is
    /// omitted with the fixed reason (or the whole untracked section is its counts-only form), and nothing is called complete.</summary>
    private static void AssertStated(string manifest)
    {
        using var document = JsonDocument.Parse(manifest);
        var evidence = document.RootElement.GetProperty("changeEvidence");
        var changed = evidence.GetProperty("changedPaths").EnumerateArray().Select(path => path.GetProperty("Path").GetString()).ToArray();
        Assert.Contains("AGENTS.md", changed);
        Assert.Contains("CLAUDE.md", changed);
        Assert.True(evidence.GetProperty("diffTruncated").GetBoolean());
        var reserved = evidence.GetProperty("diffSelection").GetProperty("reservedInstructionFiles").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(["AGENTS.md", "CLAUDE.md"], reserved);
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
