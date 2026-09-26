using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationMessageEvidence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class AttemptInputMessageCoherenceTests
{
    [Fact]
    public void An_empty_sequence_is_trivially_gapless()
    {
        Assert.True(AttemptInputMessageCoherence.IsGaplessFromZero([]));
    }

    [Fact]
    public void A_single_zero_is_gapless()
    {
        Assert.True(AttemptInputMessageCoherence.IsGaplessFromZero([0]));
    }

    [Fact]
    public void A_contiguous_run_from_zero_is_gapless()
    {
        Assert.True(AttemptInputMessageCoherence.IsGaplessFromZero([0, 1, 2, 3, 4, 5]));
    }

    [Fact]
    public void A_missing_middle_value_is_a_gap()
    {
        Assert.False(AttemptInputMessageCoherence.IsGaplessFromZero([0, 1, 3]));
    }

    [Fact]
    public void A_missing_first_value_is_a_gap()
    {
        Assert.False(AttemptInputMessageCoherence.IsGaplessFromZero([1, 2]));
    }

    [Fact]
    public void A_repeated_value_instead_of_advancing_is_a_duplicate()
    {
        Assert.False(AttemptInputMessageCoherence.IsGaplessFromZero([0, 0, 1]));
    }

    [Fact]
    public void A_trailing_duplicate_is_still_rejected()
    {
        Assert.False(AttemptInputMessageCoherence.IsGaplessFromZero([0, 1, 1]));
    }
}
