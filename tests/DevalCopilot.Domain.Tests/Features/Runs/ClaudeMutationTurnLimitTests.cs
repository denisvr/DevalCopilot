using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class ClaudeMutationTurnLimitTests
{
    [Fact]
    public void The_bounds_are_one_through_one_hundred()
    {
        Assert.Equal(1, ClaudeMutationTurnLimit.Minimum);
        Assert.Equal(100, ClaudeMutationTurnLimit.Maximum);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(12)]
    [InlineData(99)]
    [InlineData(100)]
    public void IsValid_accepts_null_and_the_inclusive_range(int? value)
    {
        Assert.True(ClaudeMutationTurnLimit.IsValid(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void IsValid_rejects_everything_outside_the_range(int? value)
    {
        Assert.False(ClaudeMutationTurnLimit.IsValid(value));
    }
}
