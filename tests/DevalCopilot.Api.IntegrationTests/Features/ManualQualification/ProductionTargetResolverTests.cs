using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>
/// The binding to the real production target resolver, in this process's own environment. The search path is the one thing the test
/// controls: an owned directory of stand-in <c>.exe</c> files is placed first, so the production resolver's own search (the host
/// search path before its fixed fallbacks) selects them deterministically for every capability, with no installed tool, no provider
/// and no process involved. A missing or ambiguous resolution depends on the machine's own application-data layout and is therefore
/// proved against owned resolver answers in <see cref="LaunchContextCheckTests"/>, never here.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ProductionTargetResolverTests : QualificationTestBase
{
    private readonly string? _originalPath = Environment.GetEnvironmentVariable("PATH");
    private readonly ApprovedFixture _fixture;

    public ProductionTargetResolverTests()
    {
        _fixture = new ApprovedFixture(Path.Combine(LedgerDirectory, "approved"), codexScript: false);
        Environment.SetEnvironmentVariable("PATH", Path.Combine(_fixture.Directory, "tools") + Path.PathSeparator + _originalPath);
    }

    public override void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        base.Dispose();
    }

    private static Func<Capability, LaunchResolution> Bound() =>
        ProductionTargetResolver.Bind() ?? throw new InvalidOperationException("The production resolver binding is unavailable.");

    private ApprovedLaunchSet Set(Func<ApprovedLaunch, ApprovedLaunch?> change) =>
        ApprovedLaunchSet.TryCreate([.. _fixture.Set.Launches.Select(change).OfType<ApprovedLaunch>()]).Set!;

    [Fact]
    public void The_binding_finds_exactly_the_reviewed_shape_of_the_production_resolver()
    {
        // The drift sentinel: if the resolver, its catalog or its result shape changes, the binding is null and the launch refuses.
        Assert.NotNull(ProductionTargetResolver.Bind());
    }

    [Fact]
    public void The_production_resolver_selects_the_owned_files_first_on_the_search_path_for_every_capability()
    {
        var resolve = Bound();
        var expected = new Dictionary<Capability, string>
        {
            [Capability.Git] = _fixture.Git,
            [Capability.Node] = _fixture.Node,
            [Capability.ClaudeCli] = _fixture.Claude,
            [Capability.CodexCli] = _fixture.CodexExecutable,
            [Capability.DotNetSdk] = _fixture.DotNet!,
            [Capability.GitHubCli] = _fixture.GitHub!,
            [Capability.Docker] = _fixture.Docker!,
        };

        foreach (var (capability, path) in expected)
        {
            var resolution = resolve(capability);

            Assert.Equal(LaunchResolutionKind.Direct, resolution.Kind);
            Assert.True(string.Equals(path, resolution.ExecutablePath, StringComparison.OrdinalIgnoreCase), capability.ToString());
            Assert.Null(resolution.ScriptPath);
        }
    }

    [Fact]
    public void An_approval_that_is_exactly_what_the_production_resolver_selects_agrees()
    {
        Assert.Null(LaunchContextCheck.Refusal(_fixture.Set, Bound(), () => false));
    }

    [Fact]
    public void A_node_script_codex_pin_against_the_direct_codex_the_production_resolver_selects_is_refused()
    {
        var node = _fixture.Set.Find(ApprovedLaunchSet.Node)!.Executable;
        var script = Path.Combine(_fixture.Directory, "tools", "codex.js");
        File.WriteAllBytes(script, [1, 2, 3]);
        var pinned = Set(launch => launch.Capability == ApprovedLaunchSet.Codex
            ? launch with { Executable = node, Script = new ApprovedFile(script, new string('a', 64)) }
            : launch);

        Assert.Equal("LaunchContextCodexCliMismatch", LaunchContextCheck.Refusal(pinned, Bound(), () => false));
    }

    [Fact]
    public void A_different_pinned_file_than_the_one_the_production_resolver_selects_is_a_mismatch()
    {
        var other = Path.Combine(_fixture.Directory, "other", "claude.exe");
        var pinned = Set(launch => launch.Capability == ApprovedLaunchSet.Claude
            ? launch with { Executable = launch.Executable with { Path = other } }
            : launch);

        Assert.Equal("LaunchContextClaudeCliMismatch", LaunchContextCheck.Refusal(pinned, Bound(), () => false));
    }

    [Fact]
    public void An_installed_tool_missing_from_the_approval_is_extra_for_the_production_resolver()
    {
        var pinned = Set(launch => launch.Capability == ApprovedLaunchSet.GitHub ? null : launch);

        Assert.Equal("LaunchContextGitHubCliExtra", LaunchContextCheck.Refusal(pinned, Bound(), () => false));
    }

    [Fact]
    public void The_resolution_starts_no_process_and_creates_no_directory_in_the_owned_tree()
    {
        var before = Directory.GetFileSystemEntries(_fixture.Directory, "*", SearchOption.AllDirectories).Order().ToArray();

        _ = LaunchContextCheck.Refusal(_fixture.Set, Bound(), () => false);

        Assert.Equal(before, Directory.GetFileSystemEntries(_fixture.Directory, "*", SearchOption.AllDirectories).Order().ToArray());
    }
}
