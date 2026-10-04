using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.TrackedFixture;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Builders re-derive what may be delivered from the reader's attested facts and the capture's own changed paths (ADR-0024):
/// missing, duplicated, overclaimed or incoherent facts never admit text, a raw patch is not an input at all, every tracked path is
/// accounted for exactly once in ordinal order, and untracked paths are never tracked evidence.</summary>
public sealed class TrackedChangeEvidenceTests
{
    private static TrackedChangeEvidence Derive(
        IReadOnlyList<GitWorkspaceChangedPath> paths, params GitWorkspaceTrackedFile[] facts) => TrackedChangeEvidence.Derive(paths, facts);

    private static string[] Reasons(TrackedChangeEvidence evidence) => [.. evidence.Omissions.Select(omission => $"{omission.Path}:{omission.Reason}")];

    [Fact]
    public void Attested_modified_added_and_deleted_files_become_one_comparison_in_ordinal_path_order()
    {
        var evidence = Derive(
            [Modified("b.txt"), Added("c.txt"), Deleted("a.txt")],
            Edit("b.txt", "old\n", "new\n"), Add("c.txt", "fresh\n"), Delete("a.txt", "gone\n"));

        Assert.Empty(evidence.Omissions);
        var parsed = TrackedDiffParser.Parse(evidence.Text!);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], parsed.Files.Select(file => file.Path));
        Assert.All(parsed.Files, file => Assert.Equal(TrackedDiffFileKind.Text, file.Kind));
        Assert.Equal("diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ /dev/null\n@@ -1,1 +0,0 @@\n-gone\n", parsed.Files[0].Header + parsed.Files[0].Hunks[0]);
        Assert.Contains("--- /dev/null\n+++ b/c.txt\n@@ -0,0 +1,1 @@\n+fresh\n", evidence.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Staged_only_and_staged_then_modified_additions_and_modifications_are_supported()
    {
        var evidence = Derive(
            [Staged("s.txt"), new("am.txt", null, "A", "M"), new("mm.txt", null, "M", "M"), new("md.txt", null, "M", "D"), new("dd.txt", null, "D", " ")],
            Edit("s.txt", "a\n", "b\n"), Add("am.txt", "x\n"), Edit("mm.txt", "a\n", "b\n"), Delete("md.txt", "m\n"), Delete("dd.txt", "d\n"));

        Assert.Empty(evidence.Omissions);
        Assert.Equal(5, TrackedDiffParser.Parse(evidence.Text!).Files.Count);
    }

    [Fact]
    public void A_capture_with_no_attestation_at_all_delivers_no_tracked_text_and_accounts_every_tracked_path()
    {
        var evidence = TrackedChangeEvidence.Derive([Modified("b.txt"), Untracked("u.txt"), Modified("a.txt")], null);

        Assert.Equal(string.Empty, evidence.Text);
        Assert.Equal(["a.txt:not_attested", "b.txt:not_attested"], Reasons(evidence));
    }

    [Fact]
    public void A_reader_that_returns_a_legacy_raw_patch_in_no_fact_cannot_admit_it_because_the_capture_type_carries_no_such_input()
    {
        var capture = new GitWorkspaceEvidenceResult(
            GitWorkspaceEvidenceOutcome.Success, new string('a', 40), new string('b', 64), [Modified("a.txt")],
            "diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1 +1 @@\n-old\n+RAW-PATCH-TEXT\n");

        var evidence = TrackedChangeEvidence.From(capture);

        Assert.DoesNotContain("RAW-PATCH-TEXT", evidence.Text, StringComparison.Ordinal);
        Assert.Equal(["a.txt:not_attested"], Reasons(evidence));
    }

    [Fact]
    public void Facts_for_paths_the_capture_did_not_report_as_tracked_changes_are_ignored_not_delivered()
    {
        var evidence = Derive(
            [Modified("real.txt"), Untracked("new.txt")],
            Edit("real.txt", "a\n", "b\n"), Edit("invented.txt", "a\n", "INVENTED\n"), Add("new.txt", "UNTRACKED-AS-TRACKED\n"));

        Assert.Empty(evidence.Omissions);
        Assert.DoesNotContain("INVENTED", evidence.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("UNTRACKED-AS-TRACKED", evidence.Text, StringComparison.Ordinal);
        Assert.Equal(["real.txt"], TrackedDiffParser.Parse(evidence.Text!).Files.Select(file => file.Path));
    }

    [Fact]
    public void Duplicated_facts_and_duplicated_porcelain_entries_are_incoherent_for_that_path_and_a_safe_sibling_survives()
    {
        var evidence = Derive(
            [Modified("dup.txt"), Modified("twice.txt"), new("twice.txt", null, "M", " "), Modified("ok.txt")],
            Edit("dup.txt", "a\n", "b\n"), Edit("dup.txt", "a\n", "DIFFERENT\n"), Edit("twice.txt", "a\n", "b\n"), Edit("ok.txt", "a\n", "b\n"));

        Assert.Equal(["dup.txt:attestation_incoherent", "twice.txt:attestation_incoherent"], Reasons(evidence));
        Assert.Equal(["ok.txt"], TrackedDiffParser.Parse(evidence.Text!).Files.Select(file => file.Path));
    }

    [Theory]
    [InlineData(" ", "M", true, true)]
    [InlineData("A", " ", false, true)]
    [InlineData(" ", "D", true, false)]
    public void Facts_that_claim_the_wrong_absent_side_for_the_porcelain_state_are_incoherent(string index, string worktree, bool baselineExpected, bool currentExpected)
    {
        var state = new GitWorkspaceChangedPath("f.txt", null, index, worktree);
        var wrongBaseline = baselineExpected ? new GitWorkspaceTrackedFile("f.txt", null, null, "x\n") : new GitWorkspaceTrackedFile("f.txt", null, "y\n", "x\n");
        var wrongCurrent = currentExpected ? new GitWorkspaceTrackedFile("f.txt", null, "y\n", null) : new GitWorkspaceTrackedFile("f.txt", null, "y\n", "x\n");

        Assert.Equal(["f.txt:attestation_incoherent"], Reasons(Derive([state], wrongBaseline)));
        Assert.Equal(["f.txt:attestation_incoherent"], Reasons(Derive([state], wrongCurrent)));
        Assert.Equal(["f.txt:attestation_incoherent"], Reasons(Derive([state], new GitWorkspaceTrackedFile("f.txt", null, null, null))));
    }

    [Theory]
    [InlineData("U", "U", "unmerged")]
    [InlineData("A", "A", "unmerged")]
    [InlineData("D", "D", "unmerged")]
    [InlineData("U", " ", "unmerged")]
    [InlineData(" ", "T", "unsupported_status")]
    [InlineData("T", " ", "unsupported_status")]
    [InlineData(" ", "A", "unsupported_status")]
    [InlineData("A", "D", "unsupported_status")]
    [InlineData("R", " ", "unsupported_status")]
    [InlineData("C", " ", "unsupported_status")]
    public void Unmerged_and_unsupported_states_win_over_any_text_a_reader_claims(string index, string worktree, string reason)
    {
        var evidence = Derive([new("f.txt", null, index, worktree)], Edit("f.txt", "a\n", "CLAIMED-TEXT\n"));

        Assert.DoesNotContain("CLAIMED-TEXT", evidence.Text, StringComparison.Ordinal);
        Assert.Equal([$"f.txt:{reason}"], Reasons(evidence));
    }

    [Fact]
    public void A_path_that_is_both_tracked_and_untracked_is_not_compared()
    {
        var evidence = Derive([new("f.txt", null, "D", " "), Untracked("f.txt")], Delete("f.txt", "old\n"));

        Assert.Equal(["f.txt:unsupported_status"], Reasons(evidence));
    }

    [Theory]
    [InlineData(GitWorkspaceTrackedOmission.ContainmentUnproven, "containment_unproven")]
    [InlineData(GitWorkspaceTrackedOmission.Unreadable, "unreadable")]
    [InlineData(GitWorkspaceTrackedOmission.TooLarge, "too_large")]
    [InlineData(GitWorkspaceTrackedOmission.TooManyLines, "too_many_lines")]
    [InlineData(GitWorkspaceTrackedOmission.Binary, "binary")]
    [InlineData(GitWorkspaceTrackedOmission.InvalidUtf8, "invalid_utf8")]
    [InlineData(GitWorkspaceTrackedOmission.BaselineUnavailable, "baseline_unavailable")]
    [InlineData(GitWorkspaceTrackedOmission.BaselineUnverified, "baseline_unverified")]
    [InlineData(GitWorkspaceTrackedOmission.NotRegularFile, "not_regular_file")]
    [InlineData(GitWorkspaceTrackedOmission.SymbolicLink, "symbolic_link")]
    [InlineData(GitWorkspaceTrackedOmission.Submodule, "submodule")]
    [InlineData(GitWorkspaceTrackedOmission.UnsupportedMode, "unsupported_mode")]
    [InlineData(GitWorkspaceTrackedOmission.UnencodablePath, "unencodable_path")]
    [InlineData(GitWorkspaceTrackedOmission.AggregateLimit, "aggregate_limit")]
    [InlineData(GitWorkspaceTrackedOmission.NoContentDifference, "no_content_difference")]
    [InlineData(GitWorkspaceTrackedOmission.UnsupportedStatus, "unsupported_status")]
    [InlineData(GitWorkspaceTrackedOmission.Unmerged, "unmerged")]
    [InlineData(GitWorkspaceTrackedOmission.ReservedInstructionFile, "reserved_instruction_file")]
    public void Every_reader_omission_keeps_its_fixed_reason_beside_a_safe_sibling(GitWorkspaceTrackedOmission omission, string reason)
    {
        var evidence = Derive([Modified("a.txt"), Modified("z.txt")], Omit("a.txt", omission), Edit("z.txt", "a\n", "b\n"));

        Assert.Equal([$"a.txt:{reason}"], Reasons(evidence));
        Assert.Equal(["z.txt"], TrackedDiffParser.Parse(evidence.Text!).Files.Select(file => file.Path));
    }

    [Fact]
    public void An_omission_that_still_carries_text_is_incoherent_and_the_text_is_not_delivered()
    {
        var evidence = Derive([Modified("a.txt")], new GitWorkspaceTrackedFile("a.txt", GitWorkspaceTrackedOmission.Binary, "x\n", "LEAK\n"));

        Assert.Equal(["a.txt:attestation_incoherent"], Reasons(evidence));
        Assert.DoesNotContain("LEAK", evidence.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AGENTS.md")]
    [InlineData("CLAUDE.md")]
    [InlineData("agents.md")]
    [InlineData("Claude.MD")]
    public void The_two_reserved_names_are_never_tracked_evidence_whatever_the_facts_claim(string root)
    {
        var evidence = Derive([Modified(root), Modified("docs/AGENTS.md")], Edit(root, "a\n", "RESERVED\n"), Edit("docs/AGENTS.md", "a\n", "NESTED-KEPT\n"));

        Assert.Equal([$"{root}:reserved_instruction_file"], Reasons(evidence));
        Assert.DoesNotContain("RESERVED", evidence.Text, StringComparison.Ordinal);
        Assert.Contains("NESTED-KEPT", evidence.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_outside_the_source_bounds_or_with_a_nul_or_an_unpaired_surrogate_is_incoherent_even_when_a_reader_claims_it()
    {
        var tooManyLines = string.Concat(Enumerable.Repeat("x\n", GitWorkspaceTrackedFile.MaxSourceLines + 1));
        var tooManyBytes = new string('y', GitWorkspaceTrackedFile.MaxSourceBytes + 1);
        var evidence = Derive(
            [Modified("a.txt"), Modified("b.txt"), Modified("c.txt"), Modified("d.txt"), Modified("e.txt")],
            Edit("a.txt", "a\n", tooManyLines), Edit("b.txt", tooManyBytes, "b\n"), Edit("c.txt", "c\n", "nul\0byte\n"),
            Edit("d.txt", "d\n", "bad\uD800surrogate\n"), Edit("e.txt", "e\n", "fine\n"));

        Assert.Equal(
            ["a.txt:attestation_incoherent", "b.txt:attestation_incoherent", "c.txt:attestation_incoherent", "d.txt:attestation_incoherent"],
            Reasons(evidence));
        Assert.Equal(["e.txt"], TrackedDiffParser.Parse(evidence.Text!).Files.Select(file => file.Path));
    }

    [Fact]
    public void The_bounds_are_inclusive_a_source_exactly_at_each_limit_is_admitted()
    {
        var maxLines = string.Concat(Enumerable.Repeat("x\n", GitWorkspaceTrackedFile.MaxSourceLines));
        var maxBytes = new string('y', GitWorkspaceTrackedFile.MaxSourceBytes);

        var lines = Derive([Modified("a.txt")], Edit("a.txt", "a\n", maxLines));
        var bytes = Derive([Modified("b.txt")], Edit("b.txt", "b\n", maxBytes));

        Assert.Empty(lines.Omissions);
        Assert.Empty(bytes.Omissions);
    }

    [Fact]
    public void The_retained_source_budget_is_spent_in_ordinal_order_and_the_rest_are_omitted_with_a_fixed_reason()
    {
        // 256 KiB (before) + 100 KiB (after) per file: the second file would exceed 512 KiB.
        var before = new string('b', GitWorkspaceTrackedFile.MaxSourceBytes);
        var after = new string('a', 100 * 1024) + "\n";
        var evidence = Derive(
            [Modified("c.txt"), Modified("a.txt"), Modified("b.txt")],
            Edit("a.txt", before, after), Edit("b.txt", before, after), Edit("c.txt", "small\n", "tiny\n"));

        Assert.Equal(["b.txt:aggregate_limit"], Reasons(evidence));
        Assert.Equal(["a.txt", "c.txt"], TrackedDiffParser.Parse(evidence.Text!).Files.Select(file => file.Path));
    }

    [Fact]
    public void Identical_before_and_after_text_is_a_fixed_omission_not_an_empty_comparison()
    {
        var evidence = Derive([Modified("a.txt")], Edit("a.txt", "same\n", "same\n"));

        Assert.Equal(["a.txt:no_content_difference"], Reasons(evidence));
        Assert.Equal(string.Empty, evidence.Text);
    }

    [Fact]
    public void A_path_that_cannot_be_encoded_is_omitted_before_any_header_is_built()
    {
        var evidence = Derive(
            [Modified("bad�name.txt"), Modified("lone\uD800.txt"), Modified(new string('p', 4097)), Modified("ok.txt")],
            Edit("bad�name.txt", "a\n", "b\n"), Edit("lone\uD800.txt", "a\n", "b\n"), Edit(new string('p', 4097), "a\n", "b\n"), Edit("ok.txt", "a\n", "b\n"));

        Assert.Equal(3, evidence.Omissions.Count);
        Assert.All(evidence.Omissions, omission => Assert.Equal("unencodable_path", omission.Reason));
        Assert.Equal(["ok.txt"], TrackedDiffParser.Parse(evidence.Text!).Files.Select(file => file.Path));
    }

    [Fact]
    public void Every_tracked_path_is_accounted_for_once_text_or_omission_never_both_and_never_twice()
    {
        IReadOnlyList<GitWorkspaceChangedPath> paths =
        [
            Modified("m.txt"), Added("a.txt"), Deleted("d.txt"), Modified("AGENTS.md"), new("t.txt", null, " ", "T"), Modified("same.txt"),
            Modified("missing.txt"), Untracked("u.txt"),
        ];

        var evidence = Derive(
            paths,
            Edit("m.txt", "1\n", "2\n"), Add("a.txt", "x\n"), Delete("d.txt", "y\n"), Edit("AGENTS.md", "1\n", "2\n"),
            Edit("t.txt", "1\n", "2\n"), Edit("same.txt", "s\n", "s\n"));

        var delivered = TrackedDiffParser.Parse(evidence.Text!).Files.Select(file => file.Path!).ToArray();
        var omitted = evidence.Omissions.Select(omission => omission.Path).ToArray();
        var tracked = paths.Where(path => path.IndexStatus != "?").Select(path => path.Path).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(tracked, delivered.Concat(omitted).Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(delivered.Intersect(omitted));
        Assert.Equal(omitted.Length, omitted.Distinct().Count());
        Assert.Equal(["AGENTS.md", "missing.txt", "same.txt", "t.txt"], omitted);
    }

    [Fact]
    public void An_empty_capture_and_untracked_only_captures_have_no_tracked_evidence()
    {
        var none = Derive([]);
        var untrackedOnly = Derive([Untracked("u.txt")]);

        Assert.Equal(string.Empty, none.Text);
        Assert.Empty(none.Omissions);
        Assert.Equal(string.Empty, untrackedOnly.Text);
        Assert.Empty(untrackedOnly.Omissions);
    }

    [Fact]
    public void The_derivation_is_deterministic_for_identical_input_and_independent_of_fact_order()
    {
        GitWorkspaceChangedPath[] paths = [Modified("b.txt"), Modified("a.txt")];
        var one = TrackedChangeEvidence.Derive(paths, [Edit("a.txt", "1\n", "2\n"), Edit("b.txt", "3\n", "4\n")]);
        var two = TrackedChangeEvidence.Derive(paths, [Edit("b.txt", "3\n", "4\n"), Edit("a.txt", "1\n", "2\n")]);

        Assert.Equal(one.Text, two.Text);
        Assert.Equal(one.Omissions, two.Omissions);
    }
}
