using System.Diagnostics;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>
/// The launch-context check inside the real entry and the real composition, with owned stand-in files and owned resolver answers
/// only: a mismatch refuses before any fixture root, ledger entry, process slot or environment exists, an exact match proceeds to the
/// session, and the deadline decides during resolution without the waiter ever running the resolution.
/// </summary>
public sealed class LaunchContextEntryTests : QualificationTestBase
{
    private static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(3);

    private readonly string _work = Path.Combine(Path.GetTempPath(), $"devalcopilot-xunit-context-{Guid.NewGuid():N}");
    private int _resolved;
    private int _built;

    public LaunchContextEntryTests()
    {
        Directory.CreateDirectory(Temp);
    }

    /// <summary>The directory the composition is given as its temporary directory: a disposable root would be created inside it.</summary>
    private string Temp => Path.Combine(_work, "temp");

    public override void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }

        base.Dispose();
    }

    private static string? Confirmed(string name) =>
        name == QualificationOptIn.ConfirmationVariable ? QualificationOptIn.ConfirmationPhrase : null;

    private string Manifest(ApprovedFixture fixture)
    {
        var entries = fixture.Set.Launches.Select(launch =>
        {
            var entry = new Dictionary<string, string>
            {
                ["capability"] = launch.Capability,
                ["path"] = launch.Executable.Path,
                ["sha256"] = launch.Executable.Sha256,
            };
            if (launch.Script is { } script)
            {
                entry["scriptPath"] = script.Path;
                entry["scriptSha256"] = script.Sha256;
            }

            return entry;
        });
        var path = Path.Combine(_work, "approved-launches.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { approvedLaunches = entries }));
        return path;
    }

    private ApprovedFixture Fixture(bool codexScript = false, bool withOptional = true) =>
        new(Path.Combine(_work, "approved"), codexScript, withOptional);

    /// <summary>The entry with the real composition: the manifest is loaded and verified, and the owned resolver answers stand in for the
    /// production one. A returned environment is a scripted double, so an agreeing context proceeds without a host or a provider.</summary>
    private Task<int> RunEntryAsync(
        ApprovedFixture fixture,
        Dictionary<Capability, LaunchResolution> resolutions,
        StringWriter output,
        StringWriter error,
        QualificationLimits? limits = null,
        Func<Capability, LaunchResolution>? resolver = null)
    {
        var manifest = Manifest(fixture);
        Func<Capability, LaunchResolution> resolve = resolver ?? (capability =>
        {
            Interlocked.Increment(ref _resolved);
            return resolutions[capability];
        });
        return LauncherEntry.RunAsync(
            [QualificationOptIn.Flag, ApprovedLaunchManifest.Option, manifest],
            Confirmed,
            Ledger,
            limits ?? Tiny,
            deadline =>
            {
                var composed = Profile01Composition.CreateFromManifest(manifest, Ledger, Temp, deadline, () => resolve);
                Interlocked.Increment(ref _built);
                composed.DisposeAsync().AsTask().GetAwaiter().GetResult();
                return new ScriptedEnvironment();
            },
            output,
            error,
            CancellationToken.None);
    }

    private void AssertNothingWasCreated()
    {
        Assert.Equal(0, _built);
        Assert.False(Directory.Exists(LedgerDirectory), "no ledger directory, allowance or process slot may exist");
        Assert.Empty(Directory.GetFileSystemEntries(Temp));
        Assert.False(Ledger.IsSpent());
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "direct-pin-node-script-resolution", "LaunchContextCodexCliMismatch" },
        { "script-pin-direct-resolution", "LaunchContextCodexCliMismatch" },
        { "script-path-differs", "LaunchContextCodexCliMismatch" },
        { "missing", "LaunchContextCodexCliMissing" },
        { "extra", "LaunchContextGitHubCliExtra" },
        { "ambiguous", "LaunchContextClaudeCliAmbiguous" },
        { "other-git", "LaunchContextGitMismatch" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_resolved_launch_that_is_not_the_approved_tuple_refuses_before_any_fixture_process_marker_or_environment(
        string scenario,
        string code)
    {
        var fixture = Fixture(codexScript: scenario is "script-pin-direct-resolution" or "script-path-differs", withOptional: scenario != "extra");
        var resolutions = LaunchContextCheckTests.Exact(fixture);
        switch (scenario)
        {
            case "direct-pin-node-script-resolution":
                resolutions[Capability.CodexCli] =
                    new LaunchResolution(LaunchResolutionKind.NodeScript, fixture.Node, Path.Combine(_work, "codex.js"));
                break;
            case "script-pin-direct-resolution":
                resolutions[Capability.CodexCli] = LaunchContextCheckTests.Direct(fixture.CodexExecutable);
                break;
            case "script-path-differs":
                resolutions[Capability.CodexCli] =
                    new LaunchResolution(LaunchResolutionKind.NodeScript, fixture.Node, Path.Combine(_work, "other.js"));
                break;
            case "missing":
                resolutions[Capability.CodexCli] = LaunchContextCheckTests.NotFound;
                break;
            case "extra":
                resolutions[Capability.GitHubCli] = LaunchContextCheckTests.Direct(Path.Combine(_work, "installed", "gh.exe"));
                break;
            case "ambiguous":
                resolutions[Capability.ClaudeCli] = new LaunchResolution(LaunchResolutionKind.Ambiguous);
                break;
            default:
                resolutions[Capability.Git] = LaunchContextCheckTests.Direct(Path.Combine(_work, "other", "git.exe"));
                break;
        }

        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunEntryAsync(fixture, resolutions, output, error);

        Assert.Equal(LauncherEntry.RefusedExitCode, exitCode);
        Assert.Equal($"Refused: {code}.{Environment.NewLine}", error.ToString());
        Assert.Equal(string.Empty, output.ToString());
        AssertNothingWasCreated();
    }

    [Fact]
    public async Task The_refusal_reports_neither_a_path_nor_an_environment_value()
    {
        var fixture = Fixture();
        var resolutions = LaunchContextCheckTests.Exact(fixture);
        resolutions[Capability.CodexCli] = new LaunchResolution(LaunchResolutionKind.NodeScript, fixture.Node, Path.Combine(_work, "codex.js"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        await RunEntryAsync(fixture, resolutions, output, error);

        var text = error.ToString() + output;
        Assert.DoesNotContain(_work, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.GetEnvironmentVariable("PATH")!, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_exact_match_proceeds_through_the_composition_to_the_session_and_resolves_every_capability_once(bool codexScript)
    {
        var fixture = Fixture(codexScript);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await RunEntryAsync(fixture, LaunchContextCheckTests.Exact(fixture), output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(1, _built);
        Assert.Equal(Enum.GetValues<Capability>().Length, _resolved);
        // The composition itself created no root, no process slot and no ledger entry; only the session's own allowances exist.
        Assert.Empty(Directory.GetFileSystemEntries(Temp));
        Assert.DoesNotContain(Directory.GetFiles(LedgerDirectory), file => Path.GetFileName(file).StartsWith("admission-", StringComparison.Ordinal));
        using var summary = JsonDocument.Parse(output.ToString());
        Assert.Equal("Qualified", summary.RootElement.GetProperty("result").GetString());
    }

    [Fact]
    public async Task A_missing_resolver_binding_refuses_with_its_own_closed_code_before_anything_exists()
    {
        var fixture = Fixture();
        var manifest = Manifest(fixture);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag, ApprovedLaunchManifest.Option, manifest],
            Confirmed,
            Ledger,
            Tiny,
            deadline => Profile01Composition.CreateFromManifest(manifest, Ledger, Temp, deadline, () => null),
            output,
            error,
            CancellationToken.None);

        Assert.Equal(LauncherEntry.RefusedExitCode, exitCode);
        Assert.Equal($"Refused: {LaunchContextCheck.ResolverUnavailableCode}.{Environment.NewLine}", error.ToString());
        Assert.False(Directory.Exists(LedgerDirectory));
        Assert.Empty(Directory.GetFileSystemEntries(Temp));
    }

    [Fact]
    public async Task A_manifest_refusal_comes_before_any_resolution()
    {
        var fixture = Fixture();
        var manifest = Manifest(fixture);
        ApprovedFixture.Tamper(fixture.Claude);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await LauncherEntry.RunAsync(
            [QualificationOptIn.Flag, ApprovedLaunchManifest.Option, manifest],
            Confirmed,
            Ledger,
            Tiny,
            deadline => Profile01Composition.CreateFromManifest(
                manifest,
                Ledger,
                Temp,
                deadline,
                () =>
                {
                    Interlocked.Increment(ref _resolved);
                    return _ => LaunchResolution.Unavailable;
                }),
            output,
            error,
            CancellationToken.None);

        Assert.Equal(LauncherEntry.RefusedExitCode, exitCode);
        Assert.Equal($"Refused: FileIdentityDrift.{Environment.NewLine}", error.ToString());
        Assert.Equal(0, _resolved);
    }

    [Fact]
    public async Task The_deadline_decides_during_resolution_and_nothing_follows_the_late_release()
    {
        var fixture = Fixture();
        var resolutions = LaunchContextCheckTests.Exact(fixture);
        using var release = new ManualResetEventSlim();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var clock = Stopwatch.StartNew();

        var exitCode = await RunEntryAsync(
            fixture,
            resolutions,
            output,
            error,
            Tiny with { Outer = TimeSpan.FromMilliseconds(300) },
            capability =>
            {
                var call = Interlocked.Increment(ref _resolved);
                if (call == 2)
                {
                    release.Wait();
                }

                return resolutions[capability];
            });

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exitCode);
        Assert.True(clock.Elapsed < Elapsed, "the entry returned only after the held resolution");
        release.Set();
        await Task.Delay(500);

        // The verdict emission is bounded by the reserve and may complete after the return under load, so it is read after the wait.
        using var verdict = JsonDocument.Parse(output.ToString());
        Assert.Equal("OuterDeadlineExceeded", verdict.RootElement.GetProperty("code").GetString());

        // The held resolution finished after the bound: no further capability was resolved, and no environment, root, slot or
        // allowance came from it.
        Assert.Equal(2, _resolved);
        Assert.Equal(0, _built);
        Assert.Empty(Directory.GetFileSystemEntries(Temp));
        Assert.False(Ledger.IsSpent());
        Assert.False(Ledger.IsConsumed(AllowanceRole.Planner));
    }

    [Fact]
    public async Task A_deadline_that_elapses_before_the_context_check_resolves_nothing()
    {
        var fixture = Fixture();
        var resolutions = LaunchContextCheckTests.Exact(fixture);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var limits = Tiny with { Outer = TimeSpan.FromMilliseconds(1) };

        var exitCode = await RunEntryAsync(fixture, resolutions, output, error, limits);
        await Task.Delay(300);

        Assert.Equal(LauncherEntry.OuterDeadlineExitCode, exitCode);
        Assert.Equal(0, _resolved);
        Assert.Equal(0, _built);
        Assert.Empty(Directory.GetFileSystemEntries(Temp));
        Assert.False(Ledger.IsSpent());
    }
}
