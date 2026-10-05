using System.Text;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Queries.GetGitCheckpointDiff;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>The checkpoint-inspection response policy (ADR-0027): whole file blocks in ordinal order within 512 KiB of UTF-8, an
/// over-budget block is omitted whole with a fixed reason, never cut, and the metadata states exactly what was delivered.</summary>
public sealed class CheckpointComparisonFitTests
{
    private const string Fingerprint = "f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1";

    private static AttestedTrackedComparison.Entry Block(string path, int bytes) =>
        new(path, "diff --git a/" + path + "\n" + new string('x', bytes - ("diff --git a/" + path + "\n").Length), null);

    private static AttestedTrackedComparison.Entry Omitted(string path, string reason) => new(path, null, reason);

    [Fact]
    public void No_tracked_path_is_a_complete_empty_comparison()
    {
        var result = CheckpointComparisonFit.Fit(Fingerprint, []);

        Assert.Equal(string.Empty, result.ComparisonText);
        Assert.True(result.IsComplete);
        Assert.Equal(0, result.TrackedPathCount);
        Assert.Equal(0, result.ComparedPathCount);
        Assert.Empty(result.Omissions);
        Assert.Equal(Fingerprint, result.FingerprintSha256);
        Assert.False(string.IsNullOrWhiteSpace(result.Limitation));
    }

    [Fact]
    public void Every_block_that_fits_is_delivered_in_order_and_the_result_is_complete()
    {
        var result = CheckpointComparisonFit.Fit(Fingerprint, [Block("a", 100), Block("b", 200)]);

        Assert.StartsWith("diff --git a/a\n", result.ComparisonText, StringComparison.Ordinal);
        Assert.Equal(300, Encoding.UTF8.GetByteCount(result.ComparisonText));
        Assert.True(result.IsComplete);
        Assert.Equal(2, result.TrackedPathCount);
        Assert.Equal(2, result.ComparedPathCount);
    }

    [Fact]
    public void A_block_exactly_filling_the_limit_fits_and_the_next_byte_does_not()
    {
        var fits = CheckpointComparisonFit.Fit(Fingerprint, [Block("a", CheckpointComparisonFit.MaxComparisonBytes)]);
        var over = CheckpointComparisonFit.Fit(Fingerprint, [Block("a", CheckpointComparisonFit.MaxComparisonBytes + 1)]);

        Assert.True(fits.IsComplete);
        Assert.Equal(CheckpointComparisonFit.MaxComparisonBytes, Encoding.UTF8.GetByteCount(fits.ComparisonText));
        Assert.Equal(string.Empty, over.ComparisonText);
        Assert.False(over.IsComplete);
        Assert.Equal(new GetGitCheckpointDiffOmission("a", "comparison_limit"), Assert.Single(over.Omissions));
        Assert.Equal(0, over.ComparedPathCount);
    }

    [Fact]
    public void An_over_budget_block_is_omitted_whole_and_a_later_smaller_block_may_still_fit()
    {
        var result = CheckpointComparisonFit.Fit(
            Fingerprint,
            [Block("a", 300 * 1024), Block("b", 300 * 1024), Block("c", 100 * 1024), Omitted("d", "binary"), Block("e", 200 * 1024)]);

        Assert.Equal(
            [new GetGitCheckpointDiffOmission("b", "comparison_limit"), new GetGitCheckpointDiffOmission("d", "binary"),
                new GetGitCheckpointDiffOmission("e", "comparison_limit")],
            result.Omissions);
        Assert.Equal(400 * 1024, Encoding.UTF8.GetByteCount(result.ComparisonText));
        Assert.DoesNotContain("diff --git a/b\n", result.ComparisonText, StringComparison.Ordinal);
        Assert.Contains("diff --git a/c\n", result.ComparisonText, StringComparison.Ordinal);
        Assert.False(result.IsComplete);
        Assert.Equal(5, result.TrackedPathCount);
        Assert.Equal(2, result.ComparedPathCount);
    }

    [Fact]
    public void An_all_omitted_capture_is_never_complete()
    {
        var result = CheckpointComparisonFit.Fit(Fingerprint, [Omitted("a", "containment_unproven"), Omitted("b", "not_attested")]);

        Assert.Equal(string.Empty, result.ComparisonText);
        Assert.False(result.IsComplete);
        Assert.Equal(2, result.TrackedPathCount);
        Assert.Equal(0, result.ComparedPathCount);
        Assert.Equal(["a", "b"], result.Omissions.Select(omission => omission.Path));
    }

    [Fact]
    public void The_limitation_is_fixed_and_states_what_the_comparison_is_not()
    {
        var limitation = CheckpointComparisonFit.Fit(Fingerprint, []).Limitation;

        Assert.Contains("not Git's minimal or filter-normalized patch", limitation, StringComparison.Ordinal);
        Assert.Contains("modes and renames are not compared", limitation, StringComparison.Ordinal);
        Assert.Equal(limitation, CheckpointComparisonFit.Fit(Fingerprint, [Omitted("a", "binary")]).Limitation);
    }
}
