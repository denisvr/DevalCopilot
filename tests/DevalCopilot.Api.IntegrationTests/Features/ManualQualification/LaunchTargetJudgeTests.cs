using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>The launch-target proof, against real files laid out like real installations and like the owned fixture.</summary>
public sealed class LaunchTargetJudgeTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), $"devalcopilot-xunit-judge-{Guid.NewGuid():N}");
    private readonly string _owned;
    private readonly string _launcher;
    private readonly LaunchTargetJudge _judge;

    public LaunchTargetJudgeTests()
    {
        _owned = Directory.CreateDirectory(Path.Combine(_base, "owned")).FullName;
        _launcher = Directory.CreateDirectory(Path.Combine(_base, "launcher")).FullName;
        _judge = new LaunchTargetJudge(_owned, _launcher);
    }

    public void Dispose()
    {
        Directory.Delete(_base, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Touch(string relative)
    {
        var path = Path.Combine(_base, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    private ProviderTargetFact InstalledCodex(string version = "0.43.0") => new(
        Capability.CodexCli, CapabilityProbeReason.None, CapabilityLaunchKind.NodeScript,
        Touch("nodejs/node.exe"), Touch("npm/node_modules/@openai/codex/bin/codex.js"), version);

    private ProviderTargetFact InstalledClaude(string version = "2.1.0") => new(
        Capability.ClaudeCli, CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable,
        Touch("claude-home/claude.exe"), null, version);

    [Fact]
    public void Installed_provider_shapes_with_observed_versions_are_accepted()
    {
        var judgement = _judge.Judge([InstalledCodex(), InstalledClaude()]);

        Assert.True(judgement.Accepted);
        Assert.Equal(["NodeScript", "DirectExecutable"], judgement.Targets.Select(target => target.LaunchKind));
        Assert.Equal(["0.43.0", "2.1.0"], judgement.Targets.Select(target => target.Version));
    }

    [Fact]
    public void A_target_inside_the_owned_root_or_the_launcher_output_is_a_fixture()
    {
        var inRoot = new ProviderTargetFact(
            Capability.CodexCli, CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable,
            Touch("owned/bin/codex.exe"), null, "1.2.3");
        var inLauncher = new ProviderTargetFact(
            Capability.ClaudeCli, CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable,
            Touch("launcher/claude.exe"), null, "2.1.0");

        var judgement = _judge.Judge([inRoot, inLauncher]);

        Assert.False(judgement.Accepted);
        Assert.Equal(["FixtureLocation", "FixtureLocation"], judgement.Targets.Select(target => target.Code));
        Assert.All(judgement.Targets, target => Assert.Null(target.Version));
    }

    [Fact]
    public void A_provider_name_beside_the_fixture_executable_is_a_fixture_wherever_it_lives()
    {
        var codex = new ProviderTargetFact(
            Capability.CodexCli, CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable,
            Touch("elsewhere/codex.exe"), null, "1.2.3");
        Touch("elsewhere/ProviderFixture.dll");

        var judgement = _judge.Judge([codex, InstalledClaude()]);

        Assert.Equal("FixtureBinary", judgement.Targets[0].Code);
        Assert.Equal("Real", judgement.Targets[1].Code);
        Assert.False(judgement.Accepted);
    }

    [Theory]
    [InlineData(CapabilityProbeReason.ExecutableNotFound, "NotObservedExecutableNotFound")]
    [InlineData(CapabilityProbeReason.LaunchTargetAmbiguous, "NotObservedLaunchTargetAmbiguous")]
    [InlineData(CapabilityProbeReason.NeverProbed, "NotObservedNeverProbed")]
    public void A_target_the_host_did_not_observe_is_reported_with_its_closed_reason_and_no_version(CapabilityProbeReason reason, string code)
    {
        var codex = new ProviderTargetFact(Capability.CodexCli, reason, null, null, null, null);

        var judgement = _judge.Judge([codex, InstalledClaude()]);

        Assert.False(judgement.Accepted);
        Assert.Equal(code, judgement.Targets[0].Code);
        Assert.Null(judgement.Targets[0].Version);
        Assert.Equal("codex.NotObserved" + reason, judgement.Code);
    }

    [Fact]
    public void A_missing_capability_row_is_not_observed()
    {
        var judgement = _judge.Judge([InstalledClaude()]);

        Assert.Equal("NotObserved", judgement.Targets[0].Code);
        Assert.False(judgement.Accepted);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 1.2.3")]
    [InlineData("1.2.3\nextra")]
    [InlineData("C:/Users/someone/tool")]
    [InlineData("-1.2.3")]
    public void A_version_that_is_not_a_plain_token_is_never_reported(string version)
    {
        var judgement = _judge.Judge([InstalledCodex(version), InstalledClaude()]);

        Assert.Equal("VersionUnrecognized", judgement.Targets[0].Code);
        Assert.Null(judgement.Targets[0].Version);
    }

    [Fact]
    public void A_missing_file_or_a_relative_path_is_a_missing_target()
    {
        var missing = new ProviderTargetFact(
            Capability.CodexCli, CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable,
            Path.Combine(_base, "nowhere", "codex.exe"), null, "1.2.3");
        var relative = new ProviderTargetFact(
            Capability.ClaudeCli, CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable, "claude.exe", null, "2.1.0");

        var judgement = _judge.Judge([missing, relative]);

        Assert.Equal(["TargetMissing", "TargetMissing"], judgement.Targets.Select(target => target.Code));
    }

    [Fact]
    public void An_unexpected_launch_shape_or_package_is_refused()
    {
        var wrongName = new ProviderTargetFact(
            Capability.CodexCli, CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable,
            Touch("tools/not-codex.exe"), null, "1.2.3");
        var wrongPackage = new ProviderTargetFact(
            Capability.ClaudeCli, CapabilityProbeReason.None, CapabilityLaunchKind.NodeScript,
            Touch("nodejs/node.exe"), Touch("npm/node_modules/@someone/else/cli.js"), "2.1.0");
        var directWithScript = new ProviderTargetFact(
            Capability.ClaudeCli, CapabilityProbeReason.None, CapabilityLaunchKind.DirectExecutable,
            Touch("tools2/claude.exe"), Touch("tools2/script.js"), "2.1.0");

        Assert.Equal("UnexpectedLaunchShape", _judge.Judge([wrongName, wrongPackage]).Targets[0].Code);
        Assert.Equal("UnexpectedLaunchShape", _judge.Judge([wrongName, wrongPackage]).Targets[1].Code);
        Assert.Equal("UnexpectedLaunchShape", _judge.Judge([wrongName, directWithScript]).Targets[1].Code);
    }
}
