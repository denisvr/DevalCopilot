using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Proves the launch-target construction boundary itself rejects an invalid component, rather
/// than relying solely on <see cref="PackageEntrypointResolver"/> or
/// <see cref="HostExecutableResolver"/> having behaved correctly. A closed hierarchy with exactly
/// two sealed cases also means an "undefined launch kind" cannot be expressed at all — there is
/// no third case and no way to instantiate the abstract base.
/// </summary>
public sealed class ProviderLaunchTargetTests
{
    [Fact]
    public void DirectExecutable_accepts_an_absolute_path()
    {
        var target = new ProviderLaunchTarget.DirectExecutable(@"C:\Program Files\Git\cmd\git.exe");

        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", target.ExecutablePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("git.exe")]
    [InlineData(@"relative\git.exe")]
    [InlineData(@".\git.exe")]
    public void DirectExecutable_rejects_a_blank_or_relative_path(string invalidPath)
    {
        Assert.Throws<ArgumentException>(() => new ProviderLaunchTarget.DirectExecutable(invalidPath));
    }

    [Fact]
    public void NodeScript_accepts_two_absolute_paths()
    {
        var target = new ProviderLaunchTarget.NodeScript(
            @"C:\Program Files\nodejs\node.exe", @"C:\npm\node_modules\@openai\codex\bin\codex.js");

        Assert.Equal(@"C:\Program Files\nodejs\node.exe", target.NodeExecutablePath);
        Assert.Equal(@"C:\npm\node_modules\@openai\codex\bin\codex.js", target.ScriptPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("node.exe")]
    [InlineData(@"relative\node.exe")]
    public void NodeScript_rejects_a_blank_or_relative_node_executable_path(string invalidNodePath)
    {
        Assert.Throws<ArgumentException>(
            () => new ProviderLaunchTarget.NodeScript(invalidNodePath, @"C:\npm\codex\bin\codex.js"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("codex.js")]
    [InlineData(@"bin\codex.js")]
    public void NodeScript_rejects_a_blank_or_relative_script_path(string invalidScriptPath)
    {
        Assert.Throws<ArgumentException>(
            () => new ProviderLaunchTarget.NodeScript(@"C:\Program Files\nodejs\node.exe", invalidScriptPath));
    }

    [Fact]
    public void NodeScript_rejects_a_missing_node_executable_component_even_when_the_script_path_is_valid()
    {
        Assert.Throws<ArgumentException>(
            () => new ProviderLaunchTarget.NodeScript(null!, @"C:\npm\codex\bin\codex.js"));
    }

    [Fact]
    public void NodeScript_rejects_a_missing_script_component_even_when_the_node_executable_path_is_valid()
    {
        Assert.Throws<ArgumentException>(
            () => new ProviderLaunchTarget.NodeScript(@"C:\Program Files\nodejs\node.exe", null!));
    }
}
