using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

public sealed class ProbeOutputVersionParserTests
{
    [Theory]
    [InlineData("git version 2.43.0.windows.1\n", "2.43.0")]
    [InlineData("10.0.100\n", "10.0.100")]
    [InlineData("v20.11.0\n", "20.11.0")]
    [InlineData("gh version 2.40.0 (2023-12-24)\nhttps://github.com/cli/cli/releases/tag/v2.40.0\n", "2.40.0")]
    [InlineData("Docker version 24.0.7, build afdd53b\n", "24.0.7")]
    public void TryExtractVersion_extracts_the_version_shaped_substring(string output, string expectedVersion)
    {
        Assert.Equal(expectedVersion, ProbeOutputVersionParser.TryExtractVersion(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("command not found")]
    [InlineData("Usage: tool [options]")]
    public void TryExtractVersion_returns_null_for_output_with_no_version_shaped_substring(string output)
    {
        Assert.Null(ProbeOutputVersionParser.TryExtractVersion(output));
    }
}
