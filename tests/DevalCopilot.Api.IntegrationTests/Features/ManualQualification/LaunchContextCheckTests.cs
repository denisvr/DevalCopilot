using System.Text.RegularExpressions;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>The comparison of every resolved launch with the owner's manifest, against owned stand-in files and owned resolver
/// answers only: no installed tool is read and no process exists. The direct-versus-script distinction, both paths of a script launch
/// and the closed, capability-specific, path-free codes are the subject.</summary>
public sealed partial class LaunchContextCheckTests : QualificationTestBase
{
    private ApprovedFixture Fixture(bool codexScript, bool withOptional = true) =>
        new(Path.Combine(LedgerDirectory, "approved"), codexScript, withOptional);

    /// <summary>The resolution that exactly matches the fixture's approval, per capability.</summary>
    internal static Dictionary<Capability, LaunchResolution> Exact(ApprovedFixture fixture)
    {
        var resolutions = new Dictionary<Capability, LaunchResolution>
        {
            [Capability.Git] = Direct(fixture.Git),
            [Capability.Node] = Direct(fixture.Node),
            [Capability.ClaudeCli] = Direct(fixture.Claude),
            [Capability.CodexCli] = fixture.CodexScript is null
                ? Direct(fixture.CodexExecutable)
                : new LaunchResolution(LaunchResolutionKind.NodeScript, fixture.CodexExecutable, fixture.CodexScript),
        };
        resolutions[Capability.DotNetSdk] = fixture.DotNet is null ? NotFound : Direct(fixture.DotNet);
        resolutions[Capability.GitHubCli] = fixture.GitHub is null ? NotFound : Direct(fixture.GitHub);
        resolutions[Capability.Docker] = fixture.Docker is null ? NotFound : Direct(fixture.Docker);
        return resolutions;
    }

    internal static LaunchResolution Direct(string path) => new(LaunchResolutionKind.Direct, path);

    internal static LaunchResolution NotFound => new(LaunchResolutionKind.NotFound);

    private static string? Check(ApprovedFixture fixture, Dictionary<Capability, LaunchResolution> resolutions) =>
        LaunchContextCheck.Refusal(fixture.Set, capability => resolutions[capability], () => false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_resolution_that_is_exactly_the_approval_agrees_for_a_direct_and_for_a_node_script_codex(bool codexScript)
    {
        var fixture = Fixture(codexScript);

        Assert.Null(Check(fixture, Exact(fixture)));
    }

    [Fact]
    public void A_manifest_without_the_optional_tools_agrees_when_the_resolver_finds_none_of_them()
    {
        var fixture = Fixture(codexScript: false, withOptional: false);

        Assert.Null(Check(fixture, Exact(fixture)));
    }

    [Fact]
    public void A_direct_codex_pin_against_a_node_script_resolution_is_refused()
    {
        var fixture = Fixture(codexScript: false);
        var resolutions = Exact(fixture);
        resolutions[Capability.CodexCli] = new LaunchResolution(LaunchResolutionKind.NodeScript, fixture.Node, @"C:\anywhere\codex.js");

        Assert.Equal("LaunchContextCodexCliMismatch", Check(fixture, resolutions));
    }

    [Fact]
    public void A_node_script_codex_pin_against_a_direct_resolution_is_refused()
    {
        var fixture = Fixture(codexScript: true);
        var resolutions = Exact(fixture);
        resolutions[Capability.CodexCli] = Direct(fixture.CodexExecutable);

        Assert.Equal("LaunchContextCodexCliMismatch", Check(fixture, resolutions));
    }

    [Fact]
    public void Both_paths_of_a_script_launch_must_agree_with_the_pin()
    {
        var fixture = Fixture(codexScript: true);
        var otherNode = new LaunchResolution(LaunchResolutionKind.NodeScript, fixture.Node + ".other", fixture.CodexScript);
        var otherScript = new LaunchResolution(LaunchResolutionKind.NodeScript, fixture.Node, fixture.CodexScript + ".other");

        foreach (var wrong in new[] { otherNode, otherScript })
        {
            var resolutions = Exact(fixture);
            resolutions[Capability.CodexCli] = wrong;

            Assert.Equal("LaunchContextCodexCliMismatch", Check(fixture, resolutions));
        }
    }

    [Fact]
    public void A_path_that_differs_only_in_case_is_the_same_file_as_on_windows()
    {
        var fixture = Fixture(codexScript: false);
        var resolutions = Exact(fixture);
        resolutions[Capability.ClaudeCli] = Direct(fixture.Claude.ToUpperInvariant());

        Assert.Null(Check(fixture, resolutions));
    }

    [Theory]
    [InlineData(Capability.Git, "LaunchContextGitMismatch")]
    [InlineData(Capability.ClaudeCli, "LaunchContextClaudeCliMismatch")]
    [InlineData(Capability.Node, "LaunchContextNodeMismatch")]
    [InlineData(Capability.DotNetSdk, "LaunchContextDotNetSdkMismatch")]
    [InlineData(Capability.GitHubCli, "LaunchContextGitHubCliMismatch")]
    [InlineData(Capability.Docker, "LaunchContextDockerMismatch")]
    public void A_different_file_than_the_approved_one_is_a_capability_specific_mismatch(Capability capability, string code)
    {
        var fixture = Fixture(codexScript: false);
        var resolutions = Exact(fixture);
        resolutions[capability] = Direct(Path.Combine(fixture.Directory, "elsewhere", "tool.exe"));

        Assert.Equal(code, Check(fixture, resolutions));
    }

    [Theory]
    [InlineData(Capability.Git, "LaunchContextGitMissing")]
    [InlineData(Capability.CodexCli, "LaunchContextCodexCliMissing")]
    [InlineData(Capability.ClaudeCli, "LaunchContextClaudeCliMissing")]
    [InlineData(Capability.DotNetSdk, "LaunchContextDotNetSdkMissing")]
    [InlineData(Capability.Docker, "LaunchContextDockerMissing")]
    public void An_approved_tool_the_resolver_cannot_find_is_missing(Capability capability, string code)
    {
        var fixture = Fixture(codexScript: false);
        var resolutions = Exact(fixture);
        resolutions[capability] = NotFound;

        Assert.Equal(code, Check(fixture, resolutions));
    }

    [Theory]
    [InlineData(Capability.GitHubCli, "LaunchContextGitHubCliExtra")]
    [InlineData(Capability.DotNetSdk, "LaunchContextDotNetSdkExtra")]
    [InlineData(Capability.Docker, "LaunchContextDockerExtra")]
    public void An_installed_tool_the_owner_did_not_approve_is_extra_because_the_host_would_probe_it(Capability capability, string code)
    {
        var fixture = Fixture(codexScript: false, withOptional: false);
        var resolutions = Exact(fixture);
        resolutions[capability] = Direct(Path.Combine(fixture.Directory, "installed", "tool.exe"));

        Assert.Equal(code, Check(fixture, resolutions));
    }

    [Fact]
    public void An_unapproved_node_script_resolution_is_extra_as_well()
    {
        var fixture = Fixture(codexScript: false, withOptional: false);
        var resolutions = Exact(fixture);
        resolutions[Capability.GitHubCli] = new LaunchResolution(LaunchResolutionKind.NodeScript, fixture.Node, @"C:\any\gh.js");

        Assert.Equal("LaunchContextGitHubCliExtra", Check(fixture, resolutions));
    }

    [Theory]
    [InlineData(Capability.CodexCli, "LaunchContextCodexCliAmbiguous")]
    [InlineData(Capability.ClaudeCli, "LaunchContextClaudeCliAmbiguous")]
    [InlineData(Capability.GitHubCli, "LaunchContextGitHubCliAmbiguous")]
    public void An_ambiguous_resolution_is_refused_even_for_a_capability_the_owner_did_not_approve(Capability capability, string code)
    {
        var fixture = Fixture(codexScript: false, withOptional: false);
        var resolutions = Exact(fixture);
        resolutions[capability] = new LaunchResolution(LaunchResolutionKind.Ambiguous);

        Assert.Equal(code, Check(fixture, resolutions));
    }

    [Fact]
    public void A_resolver_that_could_not_answer_refuses_with_its_own_closed_code()
    {
        var fixture = Fixture(codexScript: false);
        var resolutions = Exact(fixture);
        resolutions[Capability.Docker] = LaunchResolution.Unavailable;

        Assert.Equal(LaunchContextCheck.ResolverUnavailableCode, Check(fixture, resolutions));
    }

    [Fact]
    public void The_first_refusal_in_catalog_order_is_the_answer_so_the_providers_come_before_the_optional_tools()
    {
        var fixture = Fixture(codexScript: false);
        var resolutions = Exact(fixture);
        resolutions[Capability.Docker] = NotFound;
        resolutions[Capability.ClaudeCli] = NotFound;
        resolutions[Capability.CodexCli] = NotFound;

        Assert.Equal("LaunchContextCodexCliMissing", Check(fixture, resolutions));
    }

    [Fact]
    public void Every_catalog_capability_has_a_manifest_name_so_none_can_escape_the_comparison()
    {
        Assert.All(Enum.GetValues<Capability>(), capability => Assert.NotNull(ApprovedLaunchSet.NameOf(capability)));
    }

    [Fact]
    public void Every_refusal_is_a_closed_code_that_carries_no_path_or_environment_value()
    {
        var fixture = Fixture(codexScript: true);
        var codes = new List<string>();
        foreach (var capability in Enum.GetValues<Capability>())
        {
            foreach (var wrong in new[]
            {
                Direct(Path.Combine(fixture.Directory, "elsewhere", "tool.exe")),
                NotFound,
                new LaunchResolution(LaunchResolutionKind.Ambiguous),
                new LaunchResolution(LaunchResolutionKind.NodeScript, @"C:\other\node.exe", @"C:\other\tool.js"),
                LaunchResolution.Unavailable,
            })
            {
                var resolutions = Exact(fixture);
                resolutions[capability] = wrong;
                if (Check(fixture, resolutions) is { } code)
                {
                    codes.Add(code);
                }
            }
        }

        Assert.NotEmpty(codes);
        Assert.All(codes, code =>
        {
            Assert.Matches(ClosedCode(), code);
            Assert.DoesNotContain(Path.DirectorySeparatorChar, code);
            Assert.DoesNotContain(fixture.Directory, code, StringComparison.OrdinalIgnoreCase);
        });
    }

    [GeneratedRegex("^LaunchContext([A-Za-z]+(Missing|Extra|Ambiguous|Mismatch)|ResolverUnavailable)$")]
    private static partial Regex ClosedCode();

    [Fact]
    public void An_expired_deadline_stops_before_the_next_resolution_and_before_an_answer_is_used()
    {
        var fixture = Fixture(codexScript: false);
        var resolutions = Exact(fixture);
        // The third capability in catalog order is Claude: its answer would be a refusal, but the deadline decided first.
        resolutions[Capability.ClaudeCli] = NotFound;
        var calls = 0;
        var expiredAfter = 3;

        Assert.Throws<OperationCanceledException>(() => LaunchContextCheck.Refusal(
            fixture.Set,
            capability =>
            {
                calls++;
                return resolutions[capability];
            },
            () => calls >= expiredAfter));

        // The third answer was resolved but never compared, and no fourth resolution started.
        Assert.Equal(expiredAfter, calls);
    }

    [Fact]
    public void A_deadline_that_has_already_elapsed_resolves_nothing()
    {
        var fixture = Fixture(codexScript: false);
        var calls = 0;

        Assert.Throws<OperationCanceledException>(() => LaunchContextCheck.Refusal(
            fixture.Set,
            _ =>
            {
                calls++;
                return NotFound;
            },
            () => true));

        Assert.Equal(0, calls);
    }
}
