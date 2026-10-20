using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>ADR-0059's immutable configuration revision: only a positive revision and a limit of exactly one or two are representable,
/// the default is two, and the next revision after the largest one does not exist, so an overflowing change is refused without writing.</summary>
public sealed class ExecutionCapacityConfigurationRevisionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_supported_limit_is_recorded_with_its_revision(int limit)
    {
        var revision = ExecutionCapacityConfigurationRevision.Record(7, limit);

        Assert.Equal(7, revision.Revision);
        Assert.Equal(limit, revision.Limit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void An_unsupported_limit_is_not_representable(int limit)
    {
        Assert.False(ExecutionCapacityConfigurationRevision.IsSupportedLimit(limit));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExecutionCapacityConfigurationRevision.Record(1, limit));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void A_non_positive_revision_is_not_representable(long revision) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ExecutionCapacityConfigurationRevision.Record(revision, 2));

    [Fact]
    public void The_default_is_two_and_the_supported_range_is_one_through_two()
    {
        Assert.Equal(2, ExecutionCapacityConfigurationRevision.DefaultLimit);
        Assert.Equal(1, ExecutionCapacityConfigurationRevision.MinimumLimit);
        Assert.Equal(2, ExecutionCapacityConfigurationRevision.MaximumLimit);
    }

    [Theory]
    [InlineData(1L, 2L)]
    [InlineData(41L, 42L)]
    [InlineData(long.MaxValue - 1, long.MaxValue)]
    public void The_next_revision_follows_the_current_one(long current, long expected) =>
        Assert.Equal(expected, ExecutionCapacityConfigurationRevision.NextRevisionAfter(current));

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void No_next_revision_exists_after_overflow_or_for_an_invalid_current_one(long current) =>
        Assert.Null(ExecutionCapacityConfigurationRevision.NextRevisionAfter(current));
}
