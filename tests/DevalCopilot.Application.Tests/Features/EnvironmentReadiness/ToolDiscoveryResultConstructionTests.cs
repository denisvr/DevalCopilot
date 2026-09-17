using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.EnvironmentReadiness;

/// <summary>
/// Proves <see cref="ToolDiscoveryResult"/> is closed by construction: the only way to obtain an
/// instance is through its validated static factories, and each one rejects every malformed
/// input a caller could otherwise pass. No <c>IToolDiscoveryAdapter</c> implementation — current
/// or future — can construct an impossible combination through the public API.
/// </summary>
public sealed class ToolDiscoveryResultConstructionTests
{
    [Fact]
    public void DirectExecutableSuccess_accepts_an_absolute_path_and_a_version()
    {
        var result = ToolDiscoveryResult.DirectExecutableSuccess(@"C:\Program Files\Git\cmd\git.exe", "2.43.0");

        Assert.Equal(CapabilityProbeReason.None, result.Reason);
        Assert.Equal(CapabilityLaunchKind.DirectExecutable, result.LaunchKind);
        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", result.ResolvedExecutablePath);
        Assert.Null(result.ResolvedScriptPath);
        Assert.Equal("2.43.0", result.Version);
    }

    [Theory]
    [InlineData("", "1.0.0")]
    [InlineData("   ", "1.0.0")]
    [InlineData("git.exe", "1.0.0")]
    [InlineData(@"C:\Program Files\Git\cmd\git.exe", "")]
    [InlineData(@"C:\Program Files\Git\cmd\git.exe", "   ")]
    public void DirectExecutableSuccess_rejects_a_blank_or_relative_component(string executablePath, string version)
    {
        Assert.Throws<ArgumentException>(() => ToolDiscoveryResult.DirectExecutableSuccess(executablePath, version));
    }

    [Fact]
    public void NodeScriptSuccess_accepts_two_absolute_paths_and_a_version()
    {
        var result = ToolDiscoveryResult.NodeScriptSuccess(
            @"C:\Program Files\nodejs\node.exe", @"C:\npm\node_modules\@openai\codex\bin\codex.js", "1.0.0");

        Assert.Equal(CapabilityProbeReason.None, result.Reason);
        Assert.Equal(CapabilityLaunchKind.NodeScript, result.LaunchKind);
        Assert.Equal(@"C:\Program Files\nodejs\node.exe", result.ResolvedExecutablePath);
        Assert.Equal(@"C:\npm\node_modules\@openai\codex\bin\codex.js", result.ResolvedScriptPath);
        Assert.Equal("1.0.0", result.Version);
    }

    [Theory]
    [InlineData("", @"C:\npm\codex\bin\codex.js", "1.0.0")]
    [InlineData("node.exe", @"C:\npm\codex\bin\codex.js", "1.0.0")]
    [InlineData(@"C:\Program Files\nodejs\node.exe", "", "1.0.0")]
    [InlineData(@"C:\Program Files\nodejs\node.exe", "codex.js", "1.0.0")]
    [InlineData(@"C:\Program Files\nodejs\node.exe", @"C:\npm\codex\bin\codex.js", "")]
    public void NodeScriptSuccess_rejects_a_blank_or_relative_component(string nodePath, string scriptPath, string version)
    {
        Assert.Throws<ArgumentException>(() => ToolDiscoveryResult.NodeScriptSuccess(nodePath, scriptPath, version));
    }

    [Theory]
    [InlineData(CapabilityProbeReason.ExecutableNotFound)]
    [InlineData(CapabilityProbeReason.ExecutableInaccessible)]
    [InlineData(CapabilityProbeReason.ProbeTimedOut)]
    [InlineData(CapabilityProbeReason.VersionProbeUnparseable)]
    [InlineData(CapabilityProbeReason.LaunchTargetAmbiguous)]
    public void Failed_accepts_every_valid_failure_reason(CapabilityProbeReason reason)
    {
        var result = ToolDiscoveryResult.Failed(reason);

        Assert.Equal(reason, result.Reason);
        Assert.Null(result.LaunchKind);
        Assert.Null(result.ResolvedExecutablePath);
        Assert.Null(result.ResolvedScriptPath);
        Assert.Null(result.Version);
    }

    [Theory]
    [InlineData(CapabilityProbeReason.None)]
    [InlineData(CapabilityProbeReason.NeverProbed)]
    [InlineData(CapabilityProbeReason.ProbeInterruptedByRestart)]
    [InlineData((CapabilityProbeReason)999)]
    public void Failed_rejects_a_reason_an_adapter_never_produces(CapabilityProbeReason invalidReason)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ToolDiscoveryResult.Failed(invalidReason));
    }
}
