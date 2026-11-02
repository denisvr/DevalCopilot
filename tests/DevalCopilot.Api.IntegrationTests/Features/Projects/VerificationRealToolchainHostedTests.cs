using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Processes;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Devalente.Shared.Cqrs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

/// <summary>
/// ADR-0072 acceptance E: the real verification supervisor, mediator, EF pipeline, file-backed SQLite and sealed output store with the
/// REAL child-process adapter and the REAL fixed Windows environment profile, running this machine's real <c>dotnet.exe</c> and
/// <c>node.exe</c> with its own bundled <c>npm-cli.js</c> against owned fixtures in a disposable workspace. Only the Git observation is a
/// double, fixed at the checkpoint fingerprint, so the source binding is the recorded one. The test process carries a sentinel secret
/// that no child may see. Nothing reaches a provider, a registry or the network: the npm fixture disables its update check in its own
/// project configuration and runs only a local script.
///
/// Review WEP-R4 under ADR-0073 and WEP-F6 under ADR-0074 (<c>windows-verification-v3</c>): real .NET SDK builds and tests through the same
/// supervisor and profile. Every request passes the pre-launch <see cref="SdkSideEffectGuardAdapter"/> first.
/// A fixture library is packed by the SDK into an owned workspace feed, and the workspace's own NuGet configuration clears every other
/// source, so restore never reaches a registry, the user's package cache or a credential; the NuGet machine-settings base is the
/// profile's empty owned directory (configuration scoping, not filesystem or network isolation). Every SDK build recipe disables build
/// servers, so no persistent compiler or MSBuild node inherits the captured pipes. The test fixture is compiled by the real SDK against
/// a named framework/testhost/adapter closure copied file by file, with recorded hashes, from this test project's own build output (no
/// package cache, archive, assets, configuration or whole-tree copy), and run by the installed SDK's test platform.
/// </summary>
public sealed class VerificationRealToolchainHostedTests : IDisposable
{
    private const string Sentinel = "DEVALCOPILOT_E_SENTINEL_SECRET";
    private const string SentinelValue = "sentinel-value-e-7f3a-must-never-reach-a-verification-child";

    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Head = new('a', 40);
    private static readonly string Fingerprint = new('b', 64);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-verify-toolchain-{Guid.NewGuid():N}");
    private readonly string _databasePath;
    private readonly string _artifactRoot;
    private readonly string _scratchRoot;
    private readonly string _workspacePath;
    private readonly FilesystemArtifactStore _artifactStore;

    public VerificationRealToolchainHostedTests()
    {
        _databasePath = Path.Combine(_root, "verify.db");
        _artifactRoot = Path.Combine(_root, "artifacts");
        _scratchRoot = Path.Combine(_root, "scratch");
        _workspacePath = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspacePath);
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        Environment.SetEnvironmentVariable(Sentinel, SentinelValue);

        File.WriteAllText(Path.Combine(_workspacePath, "package.json"), """
            {
              "name": "verification-toolchain-fixture",
              "private": true,
              "scripts": {
                "check": "node check.js 0",
                "fail": "node check.js 2"
              }
            }
            """);
        // Project configuration of the fixture itself: no update check, so npm makes no registry request.
        File.WriteAllText(Path.Combine(_workspacePath, ".npmrc"), "update-notifier=false\n");
        File.WriteAllText(Path.Combine(_workspacePath, "check.js"), """
            const present = Object.prototype.hasOwnProperty.call(process.env, 'DEVALCOPILOT_E_SENTINEL_SECRET');
            console.log('sentinel=' + (present ? 'present' : 'absent'));
            console.log('nologo=' + (process.env.DOTNET_NOLOGO ?? 'unset'));
            console.log('npm-cache-owned=' + String((process.env.npm_config_cache ?? '').length > 0));
            process.exit(Number(process.argv[2] ?? '0'));
            """);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Sentinel, null);
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task A_real_dotnet_fixture_passes_under_the_fixed_profile_bound_to_its_source_and_sees_no_inherited_secret()
    {
        var execution = await RunAsync(Tool("dotnet.exe"), [FixtureDll, "print-env", Sentinel]);

        Assert.Equal(VerificationExecutionStatus.Passed, execution.Status);
        Assert.Equal(VerificationExecutionOutcome.Exited, execution.Outcome);
        Assert.Equal(0, execution.ExitCode);
        AssertProfileAndCompleteIo(execution);
        Assert.Null(execution.Diagnostic);
        Assert.Equal(Fingerprint, execution.CheckpointFingerprintSha256);
        Assert.Equal(Fingerprint, execution.CompletionFingerprintSha256);

        var stdout = await SealedStdoutAsync(execution.Id);
        Assert.Equal("<unset>", stdout.Trim());
        Assert.DoesNotContain(SentinelValue, stdout, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_scratchRoot, execution.Id.ToString("N"))), "proved scratch is released");
    }

    [Fact]
    public async Task The_profile_itself_reaches_the_real_dotnet_child()
    {
        var execution = await RunAsync(Tool("dotnet.exe"), [FixtureDll, "print-env", "DOTNET_CLI_TELEMETRY_OPTOUT"]);

        Assert.Equal(VerificationExecutionStatus.Passed, execution.Status);
        Assert.Equal("1", (await SealedStdoutAsync(execution.Id)).Trim());
    }

    [Fact]
    public async Task The_sdk_side_effect_flags_reach_the_real_dotnet_child_as_false()
    {
        // ADR-0074: guarded positive execution. The pre-launch guard admitted the request only because both flags were the literal
        // false, and the real child itself reports the same values; the ambient process carries opposite values that must not leak.
        string[] flags = ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "DOTNET_GENERATE_ASPNET_CERTIFICATE"];
        try
        {
            foreach (var flag in flags)
            {
                Environment.SetEnvironmentVariable(flag, "true");
            }

            foreach (var flag in flags)
            {
                var execution = await RunAsync(Tool("dotnet.exe"), [FixtureDll, "print-env", flag]);

                Assert.Equal(VerificationExecutionStatus.Passed, execution.Status);
                AssertProfileAndCompleteIo(execution);
                Assert.Equal("false", (await SealedStdoutAsync(execution.Id)).Trim());
            }
        }
        finally
        {
            foreach (var flag in flags)
            {
                Environment.SetEnvironmentVariable(flag, null);
            }
        }
    }

    [Fact]
    public async Task Repeated_fresh_real_sdk_executions_leave_the_persistent_user_path_identity_unchanged()
    {
        // ADR-0074 (WEP-F6): every execution gets a fresh DOTNET_CLI_HOME, so each real SDK run is a first run. Before the two flags, each
        // appended its own tools directory to the persistent user PATH. Only a read-only identity (length, entry count and a hash) is
        // compared; the value is never logged, written or restored, and a mismatch reports the identities, not the entries.
        var before = UserPathIdentity.Read();
        WriteSdkFixture();

        var pack = await RunSdkAsync(PackFixtureLibrary);
        var build = await RunSdkAsync(["build", "App/App.csproj", "--disable-build-servers"]);
        var restore = await RunSdkAsync(["restore", "App/App.csproj", "--disable-build-servers"]);

        Assert.All([pack, build, restore], execution =>
        {
            Assert.Equal(VerificationExecutionStatus.Passed, execution.Status);
            AssertProfileAndCompleteIo(execution);
        });
        Assert.Equal(3, new[] { pack.Id, build.Id, restore.Id }.Distinct().Count());
        Assert.Equal(before, UserPathIdentity.Read());
    }

    [Fact]
    public async Task A_failing_real_dotnet_fixture_is_recorded_failed_with_its_exit_code_and_no_diagnostic()
    {
        var execution = await RunAsync(Tool("dotnet.exe"), [FixtureDll, "exit-code", "3"]);

        Assert.Equal(VerificationExecutionStatus.Failed, execution.Status);
        Assert.Equal(VerificationExecutionOutcome.Exited, execution.Outcome);
        Assert.Equal(3, execution.ExitCode);
        AssertProfileAndCompleteIo(execution);
        Assert.Null(execution.Diagnostic);
    }

    [Fact]
    public async Task A_real_sdk_restore_and_build_in_one_execution_passes_with_the_owned_cache_and_no_inherited_secret()
    {
        WriteSdkFixture();
        Assert.Equal(VerificationExecutionStatus.Passed, (await RunSdkAsync(PackFixtureLibrary)).Status);

        var build = await RunSdkAsync(["build", "App/App.csproj", "--disable-build-servers"]);

        Assert.Equal(VerificationExecutionStatus.Passed, build.Status);
        Assert.Equal(0, build.ExitCode);
        AssertProfileAndCompleteIo(build);
        Assert.Null(build.Diagnostic);
        Assert.Equal(Fingerprint, build.CompletionFingerprintSha256);
        Assert.Contains("Build succeeded", await SealedStdoutAsync(build.Id), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_workspacePath, "App", "bin", "Debug", "net10.0", "App.dll")), "the build produced its assembly");
        // A library does not copy package assemblies to its output; the compile against the package and its dependency record do.
        Assert.Contains(
            "\"Fixture.Lib/1.0.0\"",
            File.ReadAllText(Path.Combine(_workspacePath, "App", "bin", "Debug", "net10.0", "App.deps.json")),
            StringComparison.Ordinal);

        // The restore used this execution's owned package folder, and that folder was released after the proved exit.
        var packageFolders = AssetsPackageFolders();
        var owned = Path.Combine(_scratchRoot, build.Id.ToString("N"));
        Assert.Single(packageFolders);
        Assert.StartsWith(owned, packageFolders[0], StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(owned));
    }

    [Fact]
    public async Task A_real_sdk_compile_error_is_recorded_failed_with_its_exit_code()
    {
        WriteSdkFixture();

        var build = await RunSdkAsync(["build", "Broken/Broken.csproj", "--disable-build-servers"]);

        Assert.Equal(VerificationExecutionStatus.Failed, build.Status);
        Assert.Equal(VerificationExecutionOutcome.Exited, build.Outcome);
        Assert.NotEqual(0, build.ExitCode);
        AssertProfileAndCompleteIo(build);
        Assert.Null(build.Diagnostic);
        Assert.Contains("error CS0029", await SealedStdoutAsync(build.Id), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_workspacePath, "Broken", "bin", "Debug", "net10.0", "Broken.dll")));
    }

    [Fact]
    public async Task A_no_restore_build_cannot_rely_on_an_earlier_executions_released_cache()
    {
        WriteSdkFixture();
        Assert.Equal(VerificationExecutionStatus.Passed, (await RunSdkAsync(PackFixtureLibrary)).Status);

        var restore = await RunSdkAsync(["restore", "App/App.csproj", "--disable-build-servers"]);
        Assert.Equal(VerificationExecutionStatus.Passed, restore.Status);
        var restoreScratch = Path.Combine(_scratchRoot, restore.Id.ToString("N"));
        Assert.StartsWith(restoreScratch, Assert.Single(AssetsPackageFolders()), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(restoreScratch), "the restore's owned cache is released after its own execution");

        // A separate execution that skips restore depends on a package folder that no longer exists.
        var noRestore = await RunSdkAsync(["build", "App/App.csproj", "--no-restore", "--disable-build-servers"]);
        Assert.Equal(VerificationExecutionStatus.Failed, noRestore.Status);
        Assert.NotEqual(0, noRestore.ExitCode);
        Assert.False(File.Exists(Path.Combine(_workspacePath, "App", "bin", "Debug", "net10.0", "App.dll")));

        // The coherent recipe restores and builds in one execution.
        var combined = await RunSdkAsync(["build", "App/App.csproj", "--disable-build-servers"]);
        Assert.Equal(VerificationExecutionStatus.Passed, combined.Status);
    }

    [Fact]
    public async Task The_sdk_derived_package_root_resolves_a_package_relative_tool_path_under_the_fixed_profile()
    {
        // WEP-F4: a real SDK restore derives $(NuGetPackageRoot) from the profile's NUGET_PACKAGES, and a target that appends a
        // package-relative path directly (as the shared NSwag target does) must find the tool the restore extracted there.
        WriteSdkFixture();
        Assert.Equal(VerificationExecutionStatus.Passed, (await RunSdkAsync(PackFixtureTool)).Status);

        var build = await RunSdkAsync(["build", "ToolApp/ToolApp.csproj", "--disable-build-servers"]);

        var stdout = await SealedStdoutAsync(build.Id);
        var ownedRoot = Path.Combine(_scratchRoot, build.Id.ToString("N"), "nuget-packages");
        Assert.True(build.Status == VerificationExecutionStatus.Passed, $"exit {build.ExitCode}; stdout:\n{stdout}");
        Assert.Equal(0, build.ExitCode);
        AssertProfileAndCompleteIo(build);
        Assert.Null(build.Diagnostic);
        Assert.Equal(Fingerprint, build.CheckpointFingerprintSha256);
        Assert.Equal(Fingerprint, build.CompletionFingerprintSha256);
        Assert.Contains("Build succeeded", stdout, StringComparison.Ordinal);

        // The actual derived root: this execution's owned package directory with a trailing separator, so the direct concatenation is a
        // real path to a tool file that exists (reported by the build itself, not an environment-string assertion).
        var root = DerivedValue(stdout, "NuGetPackageRoot");
        Assert.Equal(ownedRoot + Path.DirectorySeparatorChar, root, ignoreCase: true);
        var concatenated = DerivedValue(stdout, "ConcatenatedToolPath", out var concatenatedExists);
        Assert.Equal(root + "fixture.tool/1.0.0/tools/tool.txt", concatenated, ignoreCase: true);
        Assert.Equal("True", concatenatedExists);
        DerivedValue(stdout, "SeparatedToolPath", out var separatedExists);
        Assert.Equal("True", separatedExists);
        Assert.DoesNotContain("nuget-packagesfixture.tool", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SentinelValue, stdout, StringComparison.Ordinal);

        // Same physical owned directory as before, released with the scratch at the proved exit.
        Assert.False(Directory.Exists(Path.Combine(_scratchRoot, build.Id.ToString("N"))), "proved scratch is released");
    }

    /// <summary>Reads a value the fixture's build target reported as <c>name=[value]</c> and, when present, its <c>exists=...</c> suffix.</summary>
    private static string DerivedValue(string stdout, string name, out string exists)
    {
        var match = System.Text.RegularExpressions.Regex.Match(stdout, $@"{name}=\[(?<value>[^\]\r\n]*)\](?: exists=(?<exists>\w+))?");
        Assert.True(match.Success, $"The build did not report {name}.");
        exists = match.Groups["exists"].Value;
        return match.Groups["value"].Value;
    }

    private static string DerivedValue(string stdout, string name) => DerivedValue(stdout, name, out _);

    [Fact]
    public async Task A_missing_sdk_prerequisite_fails_the_execution_and_never_passes()
    {
        WriteSdkFixture();
        File.WriteAllText(
            Path.Combine(_workspacePath, "global.json"),
            """{ "sdk": { "version": "99.0.100", "rollForward": "disable", "allowPrerelease": false } }""");

        var build = await RunSdkAsync(["build", "Broken/Broken.csproj", "--disable-build-servers"]);

        Assert.Equal(VerificationExecutionStatus.Failed, build.Status);
        Assert.Equal(VerificationExecutionOutcome.Exited, build.Outcome);
        Assert.NotEqual(0, build.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(_workspacePath, "Broken", "obj")), "no SDK ran the build");
    }

    [Fact]
    public async Task The_sdk_reads_only_the_workspace_and_owned_nuget_configuration()
    {
        // ADR-0073: a controlled view of every NuGet configuration file the SDK would read for this workspace.
        WriteSdkFixture();

        var paths = await RunSdkAsync(["nuget", "config", "paths"]);

        Assert.Equal(VerificationExecutionStatus.Passed, paths.Status);
        AssertProfileAndCompleteIo(paths);
        var listed = (await SealedStdoutAsync(paths.Id))
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains(listed, file => string.Equals(file, Path.Combine(_workspacePath, "NuGet.config"), StringComparison.OrdinalIgnoreCase));
        var owned = Path.Combine(_scratchRoot, paths.Id.ToString("N"));
        Assert.All(listed, file => Assert.True(
            file.StartsWith(_workspacePath, StringComparison.OrdinalIgnoreCase) || file.StartsWith(owned, StringComparison.OrdinalIgnoreCase),
            "only workspace and owned configuration"));
        // The ambient user and machine NuGet configuration roots (the owned test root itself lives under the user profile).
        foreach (var ambient in new[] { "ProgramFiles(x86)", "ProgramFiles", "APPDATA" })
        {
            var value = Environment.GetEnvironmentVariable(ambient);
            if (!string.IsNullOrEmpty(value))
            {
                Assert.DoesNotContain(listed, file => file.StartsWith(value, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public async Task A_real_sdk_test_run_discovers_and_executes_passing_tests_against_the_named_closure()
    {
        var closure = await PrepareTestFixtureAsync();
        Assert.Equal(VerificationExecutionStatus.Passed, (await RunSdkAsync(BuildTestFixture)).Status);

        var run = await RunSdkAsync(TestFixtureRun("FullyQualifiedName=Fixture.Tests.Arithmetic.Adds|FullyQualifiedName=Fixture.Tests.Arithmetic.Sees_no_inherited_secret"));

        Assert.Equal(VerificationExecutionStatus.Passed, run.Status);
        Assert.Equal(VerificationExecutionOutcome.Exited, run.Outcome);
        Assert.Equal(0, run.ExitCode);
        AssertProfileAndCompleteIo(run);
        Assert.Null(run.Diagnostic);
        Assert.Equal(Fingerprint, run.CompletionFingerprintSha256);
        var stdout = await SealedStdoutAsync(run.Id);
        Assert.Contains("Passed Fixture.Tests.Arithmetic.Adds", stdout, StringComparison.Ordinal);
        Assert.Contains("Passed Fixture.Tests.Arithmetic.Sees_no_inherited_secret", stdout, StringComparison.Ordinal);
        Assert.Matches(@"Total tests: 2\s", stdout);
        Assert.Matches(@"Passed: 2\s", stdout);
        Assert.DoesNotContain(SentinelValue, stdout, StringComparison.Ordinal);
        AssertClosureUnchanged(closure);
    }

    [Fact]
    public async Task A_real_sdk_test_run_with_a_failing_assertion_is_recorded_failed_with_its_real_exit_code()
    {
        var closure = await PrepareTestFixtureAsync();
        Assert.Equal(VerificationExecutionStatus.Passed, (await RunSdkAsync(BuildTestFixture)).Status);

        var run = await RunSdkAsync(TestFixtureRun("FullyQualifiedName=Fixture.Tests.Arithmetic.Fails_on_purpose"));

        Assert.Equal(VerificationExecutionStatus.Failed, run.Status);
        Assert.Equal(VerificationExecutionOutcome.Exited, run.Outcome);
        Assert.Equal(1, run.ExitCode);
        AssertProfileAndCompleteIo(run);
        Assert.Null(run.Diagnostic);
        var stdout = await SealedStdoutAsync(run.Id);
        Assert.Contains("Failed Fixture.Tests.Arithmetic.Fails_on_purpose", stdout, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal() Failure", stdout, StringComparison.Ordinal);
        Assert.Matches(@"Total tests: 1\s", stdout);
        Assert.Matches(@"Failed: 1\s", stdout);
        AssertClosureUnchanged(closure);
    }

    [Fact]
    public async Task A_real_npm_script_through_node_and_its_bundled_cli_passes_and_sees_only_the_profile()
    {
        var execution = await RunAsync(Tool("node.exe"), [BundledNpmCli, "run", "check", "--silent"]);

        Assert.Equal(VerificationExecutionStatus.Passed, execution.Status);
        Assert.Equal(0, execution.ExitCode);
        AssertProfileAndCompleteIo(execution);
        Assert.Null(execution.Diagnostic);
        Assert.Equal(Fingerprint, execution.CompletionFingerprintSha256);

        var stdout = await SealedStdoutAsync(execution.Id);
        Assert.Contains("sentinel=absent", stdout, StringComparison.Ordinal);
        Assert.Contains("nologo=unset", stdout, StringComparison.Ordinal);
        Assert.Contains("npm-cache-owned=true", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelValue, stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failing_real_npm_script_is_recorded_failed_with_a_nonzero_exit()
    {
        var execution = await RunAsync(Tool("node.exe"), [BundledNpmCli, "run", "fail", "--silent"]);

        Assert.Equal(VerificationExecutionStatus.Failed, execution.Status);
        Assert.Equal(VerificationExecutionOutcome.Exited, execution.Outcome);
        Assert.NotEqual(0, execution.ExitCode);
        AssertProfileAndCompleteIo(execution);
        Assert.Null(execution.Diagnostic);
    }

    [Fact]
    public async Task A_direct_npm_cmd_launch_is_a_missing_prerequisite_recorded_without_a_process()
    {
        var npmCmd = Path.Combine(Path.GetDirectoryName(Tool("node.exe"))!, "npm.cmd");
        Assert.True(File.Exists(npmCmd), "the installed Node distribution ships npm.cmd");

        var execution = await RunAsync(npmCmd, ["run", "check"]);

        Assert.Equal(VerificationExecutionStatus.Failed, execution.Status);
        Assert.Equal(VerificationExecutionOutcome.Failed, execution.Outcome);
        Assert.Null(execution.ExitCode);
        Assert.Equal(VerificationExecutionDiagnostic.EnvironmentUnavailable, execution.Diagnostic);
        Assert.Null(execution.StandardOutputCompletion);
        Assert.Equal(0, await ArtifactCountAsync(execution.Id));
    }

    private static string FixtureDll => Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.dll");

    private static readonly string[] PackFixtureLibrary = ["pack", "Fixture.Lib/Fixture.Lib.csproj", "-o", "feed", "--disable-build-servers"];

    private static readonly string[] PackFixtureTool = ["pack", "Fixture.Tool/Fixture.Tool.csproj", "-o", "feed", "--disable-build-servers"];

    private static readonly string[] BuildTestFixture = ["build", "TestFixture/TestFixture.csproj", "--disable-build-servers"];

    /// <summary>
    /// The named runtime closure of the xunit framework, its VSTest adapter and the test host, taken file by file from this test
    /// project's own build output (the repository's current build): exactly these files, never a package cache, archive, assets file,
    /// configuration or the whole output tree.
    /// </summary>
    private static readonly string[] TestRuntimeClosure =
    [
        "xunit.abstractions.dll", "xunit.assert.dll", "xunit.core.dll", "xunit.execution.dotnet.dll",
        "xunit.runner.visualstudio.testadapter.dll", "xunit.runner.utility.netcoreapp10.dll", "xunit.runner.reporters.netcoreapp10.dll",
        "testhost.dll", "Microsoft.TestPlatform.CommunicationUtilities.dll", "Microsoft.TestPlatform.CrossPlatEngine.dll",
        "Microsoft.TestPlatform.Utilities.dll", "Microsoft.VisualStudio.TestPlatform.Common.dll", "Microsoft.TestPlatform.CoreUtilities.dll",
        "Microsoft.TestPlatform.PlatformAbstractions.dll", "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll", "Newtonsoft.Json.dll",
    ];

    private static string[] TestFixtureRun(string filter) =>
        ["test", "TestFixture/bin/Debug/net10.0/TestFixture.dll", "--filter", filter, "--logger", "console;verbosity=normal", "--results-directory", "test-results"];

    /// <summary>Copies the named closure into the workspace, returning each file's SHA-256 as recorded from its repository build output.</summary>
    private async Task<IReadOnlyDictionary<string, string>> PrepareTestFixtureAsync()
    {
        WriteSdkFixture();
        var closureDirectory = Path.Combine(_workspacePath, "closure");
        Directory.CreateDirectory(closureDirectory);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in TestRuntimeClosure)
        {
            var source = Path.Combine(AppContext.BaseDirectory, name);
            Assert.True(File.Exists(source), $"The repository build output lacks the closure file {name}.");
            var bytes = await File.ReadAllBytesAsync(source);
            await File.WriteAllBytesAsync(Path.Combine(closureDirectory, name), bytes);
            hashes[name] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }

        var references = string.Concat(TestRuntimeClosure.Select(name =>
            $"    <Reference Include=\"{Path.GetFileNameWithoutExtension(name)}\"><HintPath>../closure/{name}</HintPath></Reference>\n"));
        File.WriteAllText(Path.Combine(_workspacePath, "TestFixture", "TestFixture.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
            {{references}}  </ItemGroup>
            </Project>
            """);
        return hashes;
    }

    /// <summary>The built fixture carries exactly the recorded closure bytes; nothing was substituted.</summary>
    private void AssertClosureUnchanged(IReadOnlyDictionary<string, string> closure)
    {
        foreach (var (name, hash) in closure)
        {
            var built = Path.Combine(_workspacePath, "TestFixture", "bin", "Debug", "net10.0", name);
            Assert.Equal(hash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(built))));
        }
    }

    private Task<VerificationExecution> RunSdkAsync(string[] arguments) => RunAsync(Tool("dotnet.exe"), arguments, timeoutSeconds: 240);

    /// <summary>The package folders the SDK restore recorded for the App project.</summary>
    private string[] AssetsPackageFolders()
    {
        using var assets = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_workspacePath, "App", "obj", "project.assets.json")));
        return assets.RootElement.GetProperty("packageFolders").EnumerateObject().Select(folder => folder.Name).ToArray();
    }

    /// <summary>
    /// A self-contained SDK workspace: a fixture library the SDK packs into an owned local feed, an App project that restores it as a
    /// package and refuses to build if the inherited sentinel secret is visible to MSBuild, a project with a compile error, the
    /// workspace's own NuGet configuration with only the local feed, and a Directory.Build.props that stops any upward search.
    /// </summary>
    private void WriteSdkFixture()
    {
        void Write(string relative, string text)
        {
            var path = Path.Combine(_workspacePath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        Directory.CreateDirectory(Path.Combine(_workspacePath, "feed"));
        Write("Directory.Build.props", "<Project />\n");
        Write("NuGet.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="fixture-local-feed" value="feed" />
              </packageSources>
            </configuration>
            """);
        Write("Fixture.Lib/Fixture.Lib.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>Fixture.Lib</PackageId>
                <Version>1.0.0</Version>
              </PropertyGroup>
            </Project>
            """);
        Write("Fixture.Lib/Value.cs", "namespace Fixture.Lib;\n\npublic static class Value\n{\n    public const string Text = \"fixture\";\n}\n");
        Write("App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Fixture.Lib" Version="1.0.0" />
              </ItemGroup>
              <Target Name="RefuseInheritedSentinel" BeforeTargets="Build">
                <Error Condition="'$(DEVALCOPILOT_E_SENTINEL_SECRET)' != ''" Text="An inherited sentinel secret reached the build." />
              </Target>
            </Project>
            """);
        Write("App/Uses.cs", "namespace App;\n\npublic static class Uses\n{\n    public static string Read() => Fixture.Lib.Value.Text;\n}\n");
        // WEP-F4: a package whose only content is a tool file, packed by the SDK into the same owned feed, and a consumer that locates the
        // tool exactly as a shared MSBuild target does: $(NuGetPackageRoot) followed directly by the package-relative path, no separator.
        Write("Fixture.Tool/Fixture.Tool.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>Fixture.Tool</PackageId>
                <Version>1.0.0</Version>
                <IncludeBuildOutput>false</IncludeBuildOutput>
                <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
                <NoWarn>$(NoWarn);NU5128</NoWarn>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <None Include="tools/tool.txt" Pack="true" PackagePath="tools/" />
              </ItemGroup>
            </Project>
            """);
        Write("Fixture.Tool/tools/tool.txt", "fixture tool payload\n");
        Write("ToolApp/ToolApp.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Fixture.Tool" Version="1.0.0" PrivateAssets="all" />
              </ItemGroup>
              <Target Name="LocatePackageRelativeTool" BeforeTargets="Build">
                <PropertyGroup>
                  <ConcatenatedToolPath>$(NuGetPackageRoot)fixture.tool/1.0.0/tools/tool.txt</ConcatenatedToolPath>
                  <SeparatedToolPath>$([MSBuild]::EnsureTrailingSlash('$(NuGetPackageRoot)'))fixture.tool/1.0.0/tools/tool.txt</SeparatedToolPath>
                </PropertyGroup>
                <Message Importance="high" Text="NuGetPackageRoot=[$(NuGetPackageRoot)]" />
                <Message Importance="high" Text="ConcatenatedToolPath=[$(ConcatenatedToolPath)] exists=$([System.IO.File]::Exists('$(ConcatenatedToolPath)'))" />
                <Message Importance="high" Text="SeparatedToolPath=[$(SeparatedToolPath)] exists=$([System.IO.File]::Exists('$(SeparatedToolPath)'))" />
                <Error Condition="'$(DEVALCOPILOT_E_SENTINEL_SECRET)' != ''" Text="An inherited sentinel secret reached the build." />
                <Error Condition="!Exists('$(ConcatenatedToolPath)')" Text="The package-relative tool was not found at $(ConcatenatedToolPath)." />
              </Target>
            </Project>
            """);
        Write("ToolApp/Program.cs", "namespace ToolApp;\n\npublic static class Program\n{\n    public static void Main()\n    {\n    }\n}\n");
        Write("Broken/Broken.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        Write("Broken/Broken.cs", "namespace Broken;\n\npublic static class Broken\n{\n    public static int Value => \"not an int\";\n}\n");
        Write("TestFixture/Arithmetic.cs", """
            using Xunit;

            namespace Fixture.Tests;

            public sealed class Arithmetic
            {
                [Fact]
                public void Adds() => Assert.Equal(4, 2 + 2);

                [Fact]
                public void Fails_on_purpose() => Assert.Equal(5, 2 + 2);

                [Fact]
                public void Sees_no_inherited_secret() => Assert.Null(System.Environment.GetEnvironmentVariable("DEVALCOPILOT_E_SENTINEL_SECRET"));
            }
            """);
    }

    private static string BundledNpmCli =>
        Path.Combine(Path.GetDirectoryName(Tool("node.exe"))!, "node_modules", "npm", "bin", "npm-cli.js");

    /// <summary>The installed toolchain this machine builds the repository with; acceptance E requires it, so its absence fails.</summary>
    private static string Tool(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), fileName);
            if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        Assert.Fail($"Acceptance E requires an installed {fileName} on PATH.");
        return string.Empty;
    }

    private static void AssertProfileAndCompleteIo(VerificationExecution execution)
    {
        Assert.Equal(WindowsVerificationEnvironmentProvider.ProfileVersion, execution.EnvironmentProfileVersion);
        Assert.Equal(OutputCaptureCompletion.Complete, execution.StandardOutputCompletion);
        Assert.Equal(OutputCaptureCompletion.Complete, execution.StandardErrorCompletion);
        Assert.Equal(InputDeliveryCompletion.NotRedirected, execution.StandardInputDelivery);
    }

    private async Task<VerificationExecution> RunAsync(string executable, string[] arguments, int timeoutSeconds = 60)
    {
        await using (var migrate = CreateContext())
        {
            await migrate.Database.MigrateAsync();
        }

        var executionId = await SeedClaimAsync(executable, arguments, timeoutSeconds);
        await using var provider = BuildServices();
        var guard = new SdkSideEffectGuardAdapter(new ChildProcessExecutionAdapter());
        var supervisor = new VerificationExecutionSupervisor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            guard,
            new FixedFingerprintEvidence(),
            _artifactStore,
            new WindowsVerificationEnvironmentProvider(_scratchRoot),
            NullLogger<VerificationExecutionSupervisor>.Instance);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds + 60);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100);
                await using var db = CreateContext();
                var execution = await db.VerificationExecutions.AsNoTracking().SingleAsync(candidate => candidate.Id == executionId);
                if (execution.Status != VerificationExecutionStatus.Running)
                {
                    // ADR-0074 pre-launch control: a refused request is a failure of the profile, never an expected outcome of a test.
                    Assert.True(
                        guard.Refusals == 0,
                        $"The pre-launch guard refused a dotnet.exe child before it started (launches={guard.Launches}, missing={guard.LastRefusal}).");
                    return execution;
                }
            }

            throw new TimeoutException("The verification execution did not reach a terminal state.");
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await supervisor.StopAsync(stop.Token);
        }
    }

    private async Task<Guid> SeedClaimAsync(string executable, string[] arguments, int timeoutSeconds)
    {
        await using var db = CreateContext();
        // A distinct registered source per execution: several executions of one test share the workspace, never the registration.
        var project = Project.Register(Guid.NewGuid(), "Toolchain", Path.Combine(_root, $"source-{Guid.NewGuid():N}"), Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, _workspacePath, "branch-1", Head, "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, Head, Fingerprint, []);
        var recipe = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Toolchain", executable, arguments, timeoutSeconds, true, Now);
        db.Projects.Add(project);
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(checkpoint);
        db.VerificationCommands.Add(recipe);
        db.RepositoryMutationLeases.Add(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now));
        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, recipe, Now);
        db.VerificationExecutions.Add(execution);
        await db.SaveChangesAsync(CancellationToken.None);
        return execution.Id;
    }

    private async Task<string> SealedStdoutAsync(Guid executionId)
    {
        await using var db = CreateContext();
        var artifact = await db.VerificationOutputArtifacts.AsNoTracking().SingleAsync(
            candidate => candidate.VerificationExecutionId == executionId && candidate.Purpose == VerificationOutputPurpose.StandardOutput);
        Assert.Equal(OutputCaptureCompletion.Complete, artifact.CaptureCompletion);
        return await File.ReadAllTextAsync(Path.Combine(_artifactRoot, artifact.RelativeStoragePath));
    }

    private async Task<int> ArtifactCountAsync(Guid executionId)
    {
        await using var db = CreateContext();
        return await db.VerificationOutputArtifacts.CountAsync(candidate => candidate.VerificationExecutionId == executionId);
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddDevalenteMediator(typeof(ClaimVerificationExecutionCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(ClaimVerificationExecutionCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        return services.BuildServiceProvider();
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    /// <summary>A read-only identity of the persistent user PATH: never the value, only its length, entry count and SHA-256.</summary>
    private sealed record UserPathIdentity(int Length, int Entries, string Sha256)
    {
        public static UserPathIdentity Read()
        {
            var value = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
            return new UserPathIdentity(
                value.Length,
                value.Split(';', StringSplitOptions.RemoveEmptyEntries).Length,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))));
        }
    }

    /// <summary>The Git observation double: the workspace is always at the checkpoint, so the recorded binding is the claimed one.</summary>
    private sealed class FixedFingerprintEvidence : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, Head, Fingerprint, [], null));
    }
}
