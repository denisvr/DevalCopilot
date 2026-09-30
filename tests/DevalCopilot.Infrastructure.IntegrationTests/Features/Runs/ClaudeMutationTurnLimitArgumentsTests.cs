using System.Globalization;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

public sealed class ClaudeMutationTurnLimitArgumentsTests
{
    private const string ImplementationV2 = ClaudeMutationAdapterContract.ImplementationV2;
    private const string ReviewCorrectionV2 = ClaudeMutationAdapterContract.ReviewCorrectionV2;

    [Theory]
    [InlineData(ClaudeMutationAdapterContract.ImplementationV1)]
    [InlineData(ClaudeMutationAdapterContract.ImplementationV2)]
    [InlineData(ClaudeMutationAdapterContract.ReviewCorrectionV2)]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void A_null_request_appends_nothing_and_succeeds_for_any_version(string? version)
    {
        List<string> arguments = ["--print"];

        var accepted = ClaudeMutationTurnLimitArguments.TryAppend(arguments, null, version, ImplementationV2);

        Assert.True(accepted);
        Assert.Equal(["--print"], arguments);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(12)]
    [InlineData(99)]
    [InlineData(100)]
    public void A_valid_request_with_the_expected_v2_version_appends_one_discrete_pair(int limit)
    {
        List<string> arguments = ["--print", "--model", "opus"];

        var accepted = ClaudeMutationTurnLimitArguments.TryAppend(arguments, limit, ImplementationV2, ImplementationV2);

        Assert.True(accepted);
        Assert.Equal(["--print", "--model", "opus", "--max-turns", limit.ToString(CultureInfo.InvariantCulture)], arguments);
    }

    [Fact]
    public void The_review_correction_path_accepts_only_its_own_v2_version()
    {
        List<string> accepted = [];
        List<string> crossed = [];

        Assert.True(ClaudeMutationTurnLimitArguments.TryAppend(accepted, 5, ReviewCorrectionV2, ReviewCorrectionV2));
        Assert.False(ClaudeMutationTurnLimitArguments.TryAppend(crossed, 5, ImplementationV2, ReviewCorrectionV2));

        Assert.Equal(["--max-turns", "5"], accepted);
        Assert.Empty(crossed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void An_out_of_range_request_is_refused_and_appends_nothing(int limit)
    {
        List<string> arguments = ["--print"];

        var accepted = ClaudeMutationTurnLimitArguments.TryAppend(arguments, limit, ImplementationV2, ImplementationV2);

        Assert.False(accepted);
        Assert.Equal(["--print"], arguments);
    }

    [Theory]
    [InlineData(ClaudeMutationAdapterContract.ImplementationV1)]
    [InlineData("claude-implementation-v3")]
    [InlineData("CLAUDE-IMPLEMENTATION-V2")]
    [InlineData("claude-implementation-v2 ")]
    [InlineData(ClaudeMutationAdapterContract.ReviewCorrectionV2)]
    [InlineData("")]
    [InlineData(null)]
    public void A_request_with_any_other_version_is_refused_and_appends_nothing(string? version)
    {
        List<string> arguments = ["--print"];

        var accepted = ClaudeMutationTurnLimitArguments.TryAppend(arguments, 10, version, ImplementationV2);

        Assert.False(accepted);
        Assert.Equal(["--print"], arguments);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void The_number_is_culture_invariant(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            List<string> arguments = [];

            Assert.True(ClaudeMutationTurnLimitArguments.TryAppend(arguments, 100, ImplementationV2, ImplementationV2));

            Assert.Equal(["--max-turns", "100"], arguments);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
