using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

public sealed class HostCapabilityCatalogTests
{
    [Theory]
    [InlineData(Capability.Git)]
    [InlineData(Capability.GitHubCli)]
    [InlineData(Capability.DotNetSdk)]
    [InlineData(Capability.Node)]
    [InlineData(Capability.Docker)]
    public void GetPackageEntrypointDescriptor_is_null_for_a_capability_with_no_npm_package_fallback(Capability capability)
    {
        Assert.Null(HostCapabilityCatalog.GetPackageEntrypointDescriptor(capability));
    }

    [Fact]
    public void GetPackageEntrypointDescriptor_for_codex_never_includes_the_private_desktop_application_layout()
    {
        var descriptor = HostCapabilityCatalog.GetPackageEntrypointDescriptor(Capability.CodexCli);

        Assert.NotNull(descriptor);
        Assert.Equal("@openai/codex", descriptor.ExpectedPackageName);
        Assert.Equal("codex", descriptor.ExpectedCommandName);
        Assert.All(descriptor.PackageRootCandidates, candidate => Assert.Contains("npm", candidate, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetPackageEntrypointDescriptor_for_claude_targets_the_expected_package_and_command()
    {
        var descriptor = HostCapabilityCatalog.GetPackageEntrypointDescriptor(Capability.ClaudeCli);

        Assert.NotNull(descriptor);
        Assert.Equal("@anthropic-ai/claude-code", descriptor.ExpectedPackageName);
        Assert.Equal("claude", descriptor.ExpectedCommandName);
    }
}
