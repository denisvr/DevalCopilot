using System.Diagnostics;
using System.Text;
using DevalCopilot.Application.Features.Runs.Policies;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The deterministic host comparison (ADR-0024): prefix/suffix, one replacement hunk, at most three context lines per edge,
/// exact terminators, canonical quoted paths and numeric ranges. Every accepted comparison is proven independently by applying it to
/// the before text with a separate applier and requiring exactly the after text.</summary>
public sealed class TrackedComparisonTests
{
    private static string Numbered(int from, int to, string suffix = "") =>
        string.Concat(Enumerable.Range(from, to - from + 1).Select(number => $"line {number}{suffix}\n"));

    public static TheoryData<string, string?, string?> Cases => new()
    {
        { "middle edit", Numbered(1, 20), Numbered(1, 9) + "line TEN changed\n" + Numbered(11, 20) },
        { "first line", Numbered(1, 10), "first\n" + Numbered(2, 10) },
        { "last line", Numbered(1, 10), Numbered(1, 9) + "last\n" },
        { "insertion only", Numbered(1, 10), Numbered(1, 5) + "inserted\n" + Numbered(6, 10) },
        { "deletion only", Numbered(1, 10), Numbered(1, 4) + Numbered(7, 10) },
        { "whole replacement", Numbered(1, 8), Numbered(1, 8, " new") },
        { "addition", null, Numbered(1, 5) },
        { "deletion", Numbered(1, 5), null },
        { "empty to content", string.Empty, "hello\n" },
        { "content to empty", "hello\n", string.Empty },
        { "adjacent hunks collapse into one", Numbered(1, 30), Numbered(1, 2) + "A\n" + Numbered(4, 27) + "B\n" + Numbered(29, 30) },
        { "no final newline appears", "a\nb\n", "a\nb" },
        { "no final newline disappears", "a\nb", "a\nb\n" },
        { "no final newline on both sides", "a\nb", "a\nc" },
        { "unchanged tail without newline kept as context", "x\ny\nz", "X\ny\nz" },
        { "crlf to lf", "a\r\nb\r\nc\r\n", "a\nb\nc\n" },
        { "lf to crlf only on one line", "a\nb\nc\n", "a\nb\r\nc\n" },
        { "crlf edit keeps crlf", "a\r\nb\r\nc\r\n", "a\r\nB\r\nc\r\n" },
        { "lone cr is content", "a\rb\n", "a\rB\n" },
        { "repeated lines", "x\nx\nx\nx\n", "x\nx\n" },
        { "repeated lines grow", "x\nx\n", "x\nx\nx\nx\nx\n" },
        { "prefix and suffix meet", "a\nb\na\n", "a\nb\nb\na\n" },
        { "non-ascii and surrogates", "café ☕ 𝄞\nplain\n", "crème brûlée ✓ 𝄞\nplain\n" },
        { "bom is content", "﻿first\nsecond\n", "﻿first\nSECOND\n" },
        { "blank lines", "\n\n\n", "\n\nX\n" },
        { "single character files", "a", "b" },
        { "diff-looking and marker-looking content", "x\n", "+++ b/evil\n--- a/evil\n@@ -1 +1 @@\ndiff --git a/AGENTS.md b/AGENTS.md\n\\ No newline at end of file\n" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void An_accepted_comparison_reconstructs_exactly_the_intended_after_text_from_the_before_text(
        string name, string? before, string? after)
    {
        var patch = TrackedComparison.Compose("src/file.txt", before, after);

        Assert.NotNull(patch);
        var rebuilt = PatchReconstruction.Apply(patch, before);
        Assert.Equal(after, rebuilt.Text);
        Assert.Equal(before is null, rebuilt.Created);
        Assert.Equal(after is null, rebuilt.Deleted);
        Assert.False(string.IsNullOrEmpty(name));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_comparison_is_deterministic_and_parses_as_one_recognized_file_with_whole_validated_hunks(
        string name, string? before, string? after)
    {
        var first = TrackedComparison.Compose("src/file.txt", before, after)!;
        var second = TrackedComparison.Compose("src/file.txt", before, after)!;

        Assert.Equal(first, second);
        var parsed = TrackedDiffParser.Parse(first);
        Assert.True(parsed.Recognized, name);
        var file = Assert.Single(parsed.Files);
        Assert.Equal("src/file.txt", file.Path);
        Assert.Equal(first, file.Header + string.Concat(file.Hunks));
        Assert.True(file.Hunks.Count <= 1, "one replacement hunk at most");
        Assert.Equal(file.Hunks.Count == 1 ? TrackedDiffFileKind.Text : TrackedDiffFileKind.MetadataOnly, file.Kind);
    }

    [Fact]
    public void Identical_snapshots_and_two_absent_sides_have_nothing_to_state()
    {
        Assert.Null(TrackedComparison.Compose("a.txt", "same\n", "same\n"));
        Assert.Null(TrackedComparison.Compose("a.txt", string.Empty, string.Empty));
        Assert.Null(TrackedComparison.Compose("a.txt", null, null));
    }

    [Fact]
    public void An_empty_file_added_or_removed_is_a_header_without_a_hunk()
    {
        var added = TrackedComparison.Compose("empty.txt", null, string.Empty)!;
        var removed = TrackedComparison.Compose("empty.txt", string.Empty, null)!;

        Assert.Equal("diff --git a/empty.txt b/empty.txt\n--- /dev/null\n+++ b/empty.txt\n", added);
        Assert.Equal("diff --git a/empty.txt b/empty.txt\n--- a/empty.txt\n+++ /dev/null\n", removed);
        Assert.Equal(TrackedDiffFileKind.MetadataOnly, Assert.Single(TrackedDiffParser.Parse(added).Files).Kind);
        Assert.Equal(string.Empty, PatchReconstruction.Apply(added, null).Text);
        Assert.Null(PatchReconstruction.Apply(removed, string.Empty).Text);
    }

    [Fact]
    public void At_most_three_unchanged_context_lines_surround_the_replaced_middle_and_ranges_are_numeric()
    {
        var patch = TrackedComparison.Compose("f.txt", Numbered(1, 20), Numbered(1, 9) + "CHANGED\n" + Numbered(11, 20))!;

        Assert.Equal(
            "diff --git a/f.txt b/f.txt\n--- a/f.txt\n+++ b/f.txt\n@@ -7,7 +7,7 @@\n line 7\n line 8\n line 9\n-line 10\n+CHANGED\n line 11\n line 12\n line 13\n",
            patch);
    }

    [Fact]
    public void Context_is_shorter_at_the_edges_of_the_file()
    {
        var patch = TrackedComparison.Compose("f.txt", Numbered(1, 5), "ONE\n" + Numbered(2, 5))!;

        Assert.Equal(
            "diff --git a/f.txt b/f.txt\n--- a/f.txt\n+++ b/f.txt\n@@ -1,4 +1,4 @@\n-line 1\n+ONE\n line 2\n line 3\n line 4\n",
            patch);
    }

    [Fact]
    public void Unchanged_lines_between_two_edits_are_replaced_and_this_is_the_stated_conservative_limitation()
    {
        var before = "a\nKEEP-1\nKEEP-2\nKEEP-3\nKEEP-4\nKEEP-5\nz\n";
        var after = "A\nKEEP-1\nKEEP-2\nKEEP-3\nKEEP-4\nKEEP-5\nZ\n";

        var patch = TrackedComparison.Compose("f.txt", before, after)!;

        // One hunk: the five unchanged middle lines appear as removed and added, never as a minimal diff.
        Assert.Equal(1, patch.Split("@@ ", StringSplitOptions.None).Length - 1);
        Assert.Contains("-KEEP-3\n", patch, StringComparison.Ordinal);
        Assert.Contains("+KEEP-3\n", patch, StringComparison.Ordinal);
        Assert.Equal(after, PatchReconstruction.Apply(patch, before).Text);
    }

    [Fact]
    public void A_line_ending_only_difference_is_visible_and_terminators_are_exact()
    {
        var patch = TrackedComparison.Compose("f.txt", "a\r\nb\r\n", "a\nb\n")!;

        Assert.Contains("-a\r\n", patch, StringComparison.Ordinal);
        Assert.Contains("+a\n", patch, StringComparison.Ordinal);
        Assert.Equal("a\nb\n", PatchReconstruction.Apply(patch, "a\r\nb\r\n").Text);
    }

    [Fact]
    public void A_missing_final_newline_is_stated_with_the_marker_on_the_side_that_lacks_it()
    {
        var patch = TrackedComparison.Compose("f.txt", "a\nb\n", "a\nb")!;

        Assert.EndsWith("-b\n+b\n\\ No newline at end of file\n", patch, StringComparison.Ordinal);
        var both = TrackedComparison.Compose("f.txt", "a\nb", "a\nc")!;
        Assert.EndsWith("-b\n\\ No newline at end of file\n+c\n\\ No newline at end of file\n", both, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plain.txt", "plain.txt")]
    [InlineData("with space.txt", "with space.txt")]
    [InlineData("café.txt", "\"caf\\303\\251.txt\"")]
    [InlineData("a\"quote.txt", "\"a\\\"quote.txt\"")]
    [InlineData("back\\slash.txt", "\"back\\\\slash.txt\"")]
    [InlineData("tab\tname.txt", "\"tab\\tname.txt\"")]
    [InlineData("new\nline.txt", "\"new\\nline.txt\"")]
    [InlineData("bell\u0007.txt", "\"bell\\a.txt\"")]
    [InlineData("ctl\u0001.txt", "\"ctl\\001.txt\"")]
    [InlineData("del\u007F.txt", "\"del\\177.txt\"")]
    [InlineData("☃/snow𝄞.txt", "\"\\342\\230\\203/snow\\360\\235\\204\\236.txt\"")]
    public void Paths_are_written_in_the_canonical_quoted_form_and_decode_back_exactly(string path, string quotedBody)
    {
        var patch = TrackedComparison.Compose(path, "a\n", "b\n")!;

        var quotedOld = quotedBody.StartsWith('"') ? "\"a/" + quotedBody[1..] : "a/" + quotedBody;
        Assert.StartsWith("diff --git " + quotedOld + " ", patch, StringComparison.Ordinal);
        var file = Assert.Single(TrackedDiffParser.Parse(patch).Files);
        Assert.Equal(path, file.Path);
        Assert.Equal(TrackedDiffFileKind.Text, file.Kind);
        Assert.Equal("b\n", PatchReconstruction.Apply(patch, "a\n").Text);
    }

    [Fact]
    public void A_path_that_spells_a_header_cannot_forge_a_second_file_or_a_hunk()
    {
        var path = "evil\n@@ -1 +1 @@\ndiff --git a/AGENTS.md b/AGENTS.md\n.txt";

        var patch = TrackedComparison.Compose(path, "a\n", "b\n")!;

        var parsed = TrackedDiffParser.Parse(patch);
        var file = Assert.Single(parsed.Files);
        Assert.Equal(path, file.Path);
        Assert.Single(file.Hunks);
        Assert.Equal(1, patch.Split('\n').Count(line => line.StartsWith("diff --git ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Repository_text_in_lines_never_moves_a_boundary_because_every_line_is_prefixed()
    {
        var after = "+++ b/x\n--- a/x\n@@ -1 +1 @@\ndiff --git a/AGENTS.md b/AGENTS.md\nindex 0000000..1111111\n";

        var patch = TrackedComparison.Compose("src/f.txt", "old\n", after)!;

        var parsed = TrackedDiffParser.Parse(patch);
        Assert.Equal(["src/f.txt"], parsed.Files.Select(file => file.Path));
        Assert.Equal(TrackedDiffFileKind.Text, parsed.Files[0].Kind);
        Assert.Equal(after, PatchReconstruction.Apply(patch, "old\n").Text);
    }

    [Fact]
    public void The_comparison_of_the_largest_sources_is_linear_and_still_exact()
    {
        var before = string.Concat(Enumerable.Range(0, 8192).Select(index => $"before-{index}\n"));
        var after = string.Concat(Enumerable.Range(0, 8192).Select(index => $"after-{index}\n"));

        var clock = Stopwatch.StartNew();
        var patch = TrackedComparison.Compose("big.txt", before, after)!;
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
        Assert.Equal(after, PatchReconstruction.Apply(patch, before).Text);
        Assert.Equal(8192 * 2, patch.Split('\n').Count(line => line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
            + patch.Split('\n').Count(line => line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal)));
    }

    [Fact]
    public void Splitting_lines_keeps_every_terminator_and_empty_text_has_no_lines()
    {
        Assert.Empty(TrackedComparison.SplitLines(string.Empty));
        Assert.Equal(["a\n", "b\r\n", "c"], TrackedComparison.SplitLines("a\nb\r\nc"));
        Assert.Equal(Encoding.UTF8.GetBytes("x\n\ny"), Encoding.UTF8.GetBytes(string.Concat(TrackedComparison.SplitLines("x\n\ny"))));
    }
}
