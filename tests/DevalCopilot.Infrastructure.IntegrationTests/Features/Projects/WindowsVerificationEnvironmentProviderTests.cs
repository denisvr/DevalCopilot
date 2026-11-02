using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Projects;

/// <summary>
/// ADR-0072 acceptance E at the profile: the fixed key set per executable kind, values derived from the system boundary and owned
/// per-execution scratch only, no ambient value (a sentinel set in this test process never appears), the node.exe plus bundled
/// npm-cli.js rule, and truthful unavailability for every missing or unvalidated prerequisite. Fabricated tool files only.
/// </summary>
public sealed class WindowsVerificationEnvironmentProviderTests : IDisposable
{
    private const string Sentinel = "DEVALCOPILOT_TEST_SENTINEL_SECRET";

    private static readonly string[] BaseKeys = ["SystemRoot", "ComSpec", "PATH", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA"];

    // ADR-0073 plus ADR-0074: the version-3 .NET keys, exactly.
    private static readonly string[] DotNetKeys =
    [
        "DOTNET_ROOT", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO", "ProgramFiles(x86)",
        "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "DOTNET_GENERATE_ASPNET_CERTIFICATE",
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-verification-profile-{Guid.NewGuid():N}");
    private readonly string _scratch;
    private readonly WindowsVerificationEnvironmentProvider _provider;

    public WindowsVerificationEnvironmentProviderTests()
    {
        _scratch = Path.Combine(_root, "scratch");
        Directory.CreateDirectory(_root);
        _provider = new WindowsVerificationEnvironmentProvider(WindowsProcessSystemRoot.Inputs.Current, _scratch);
        Environment.SetEnvironmentVariable(Sentinel, "sentinel-value-that-must-never-reach-a-child");
    }

    [Fact]
    public void A_dotnet_executable_gets_exactly_the_base_and_dotnet_keys_from_the_system_boundary_and_owned_scratch()
    {
        var dotnet = CreateTool("dotnet-tool", "dotnet.exe");
        var executionId = Guid.NewGuid();

        var preparation = _provider.Prepare(executionId, dotnet, ["build"]);

        Assert.True(preparation.IsAvailable);
        Assert.Equal(WindowsVerificationEnvironmentProvider.ProfileVersion, preparation.ProfileVersion);
        var environment = preparation.EnvironmentVariables;
        Assert.Equal(
            BaseKeys.Concat(DotNetKeys).Order(StringComparer.OrdinalIgnoreCase),
            environment.Keys.Order(StringComparer.OrdinalIgnoreCase));

        var systemDirectory = Environment.SystemDirectory;
        Assert.Equal(Path.GetDirectoryName(systemDirectory), environment["SystemRoot"], ignoreCase: true);
        Assert.Equal(Path.Combine(systemDirectory, "cmd.exe"), environment["ComSpec"], ignoreCase: true);
        Assert.Equal(
            [Path.GetDirectoryName(dotnet)!, systemDirectory],
            environment["PATH"].Split(Path.PathSeparator),
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.GetDirectoryName(dotnet), environment["DOTNET_ROOT"]);
        Assert.Equal("1", environment["DOTNET_CLI_TELEMETRY_OPTOUT"]);
        Assert.Equal("1", environment["DOTNET_NOLOGO"]);

        var owned = Path.Combine(_scratch, executionId.ToString("N"));
        foreach (var key in new[] { "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "ProgramFiles(x86)" })
        {
            Assert.StartsWith(owned, environment[key], StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(environment[key]), key);
            Assert.Empty(Directory.EnumerateFileSystemEntries(environment[key]));
        }

        // ADR-0073: the NuGet base is a fresh, empty, physically owned directory, never an ambient Program Files value or fallback.
        var nugetBase = new DirectoryInfo(environment["ProgramFiles(x86)"]);
        Assert.Equal(Path.Combine(owned, "program-files-x86"), nugetBase.FullName, ignoreCase: true);
        Assert.Null(nugetBase.LinkTarget);
        Assert.False(nugetBase.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.NotEqual(Environment.GetEnvironmentVariable("ProgramFiles(x86)"), environment["ProgramFiles(x86)"], StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(Environment.GetEnvironmentVariable("ProgramFiles"), environment["ProgramFiles(x86)"], StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProgramFiles", environment.Keys);
        Assert.DoesNotContain(environment.Values, value =>
            string.Equals(value, Environment.GetEnvironmentVariable("ProgramFiles"), StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, Environment.GetEnvironmentVariable("ProgramFiles(x86)"), StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(environment, pair => pair.Key == Sentinel || pair.Value.Contains("sentinel-value", StringComparison.Ordinal));
        Assert.NotEqual(Environment.GetEnvironmentVariable("USERPROFILE"), environment["USERPROFILE"]);
    }

    [Fact]
    public void A_dotnet_executable_disables_the_sdk_first_run_persistent_side_effects_and_no_other_kind_receives_those_keys()
    {
        // ADR-0074 (WEP-F6): the SDK's first-run experience appends the owned tools directory to the persistent user PATH and generates
        // an ASP.NET development certificate in the user's certificate store. Both outlive the owned scratch, so dotnet.exe is told
        // not to, with the literal documented value, and the key is never added for another executable kind.
        var dotnet = CreateTool("dotnet-tool", "dotnet.exe");
        var node = CreateTool("node-tool", "node.exe");
        var npmCli = CreateFile(Path.Combine(Path.GetDirectoryName(node)!, "node_modules", "npm", "bin", "npm-cli.js"));
        var other = CreateTool("other-tool", "tool.exe");

        var environment = _provider.Prepare(Guid.NewGuid(), dotnet, ["build"]).EnvironmentVariables;

        Assert.Equal("false", environment["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"]);
        Assert.Equal("false", environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"]);
        Assert.Equal(DotNetKeys.Length + BaseKeys.Length, environment.Count);
        foreach (var candidate in new[]
        {
            _provider.Prepare(Guid.NewGuid(), node, [npmCli, "run", "build"]),
            _provider.Prepare(Guid.NewGuid(), node, [Path.Combine(_root, "script.mjs")]),
            _provider.Prepare(Guid.NewGuid(), other, []),
        })
        {
            Assert.True(candidate.IsAvailable);
            Assert.DoesNotContain("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", candidate.EnvironmentVariables.Keys);
            Assert.DoesNotContain("DOTNET_GENERATE_ASPNET_CERTIFICATE", candidate.EnvironmentVariables.Keys);
        }

        // The two flags are constants of the profile: they never reflect an ambient value of this process.
        var ambient = new[] { "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "DOTNET_GENERATE_ASPNET_CERTIFICATE" };
        try
        {
            foreach (var key in ambient)
            {
                Environment.SetEnvironmentVariable(key, "true");
            }

            var again = _provider.Prepare(Guid.NewGuid(), dotnet, ["build"]).EnvironmentVariables;
            Assert.All(ambient, key => Assert.Equal("false", again[key]));
        }
        finally
        {
            foreach (var key in ambient)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
    }

    [Fact]
    public void The_owned_nuget_package_directory_is_supplied_with_one_trailing_separator_and_nothing_else_is_normalized()
    {
        // WEP-F4: the SDK's $(NuGetPackageRoot) is NUGET_PACKAGES verbatim and shared MSBuild targets append a package-relative path to it
        // directly, so the value ends in a directory separator. This is representational only.
        var dotnet = CreateTool("dotnet-tool", "dotnet.exe");
        var executionId = Guid.NewGuid();

        var environment = _provider.Prepare(executionId, dotnet, ["build"]).EnvironmentVariables;

        var owned = Path.Combine(_scratch, executionId.ToString("N"));
        var packages = Path.Combine(owned, "nuget-packages");
        Assert.Equal(packages + Path.DirectorySeparatorChar, environment["NUGET_PACKAGES"], ignoreCase: true);
        Assert.False(environment["NUGET_PACKAGES"].EndsWith("" + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.Equal(packages + "fixture.tool", environment["NUGET_PACKAGES"].TrimEnd(Path.DirectorySeparatorChar) + "fixture.tool", ignoreCase: true);

        // The same single, fresh, physically owned directory as before: no extra or renamed child, no link.
        var directory = new DirectoryInfo(packages);
        Assert.True(directory.Exists);
        Assert.Null(directory.LinkTarget);
        Assert.False(directory.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Empty(directory.EnumerateFileSystemInfos());
        Assert.Equal(
            ["appdata", "dotnet-home", "localappdata", "nuget-packages", "profile", "program-files-x86", "temp"],
            Directory.GetDirectories(owned).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        // Only that one value is normalized: every other owned directory value keeps its plain form.
        foreach (var key in new[] { "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "DOTNET_CLI_HOME", "ProgramFiles(x86)", "DOTNET_ROOT" })
        {
            Assert.False(Path.EndsInDirectorySeparator(environment[key]), key);
        }
    }

    [Fact]
    public void Node_with_its_own_bundled_npm_cli_gets_the_npm_keys_and_empty_owned_configuration()
    {
        var node = CreateTool("node-tool", "node.exe");
        var npmCli = CreateFile(Path.Combine(Path.GetDirectoryName(node)!, "node_modules", "npm", "bin", "npm-cli.js"));

        var preparation = _provider.Prepare(Guid.NewGuid(), node, [npmCli, "run", "build"]);

        Assert.True(preparation.IsAvailable);
        var environment = preparation.EnvironmentVariables;
        Assert.Equal(
            BaseKeys.Concat(["npm_config_cache", "npm_config_userconfig", "npm_config_globalconfig"]).Order(StringComparer.OrdinalIgnoreCase),
            environment.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(0, new FileInfo(environment["npm_config_userconfig"]).Length);
        Assert.Equal(0, new FileInfo(environment["npm_config_globalconfig"]).Length);
        Assert.True(Directory.Exists(environment["npm_config_cache"]));
        Assert.DoesNotContain("DOTNET_ROOT", environment.Keys);
    }

    [Fact]
    public void Node_running_an_npm_cli_that_is_not_its_own_bundled_one_is_unavailable()
    {
        var node = CreateTool("node-tool", "node.exe");
        CreateFile(Path.Combine(Path.GetDirectoryName(node)!, "node_modules", "npm", "bin", "npm-cli.js"));
        var foreign = CreateFile(Path.Combine(_root, "elsewhere", "npm-cli.js"));

        Assert.False(_provider.Prepare(Guid.NewGuid(), node, [foreign, "run", "build"]).IsAvailable);
        Assert.False(_provider.Prepare(Guid.NewGuid(), node, ["npm-cli.js", "run", "build"]).IsAvailable);
    }

    [Fact]
    public void Node_running_another_script_and_any_other_executable_get_only_the_base_keys()
    {
        var node = CreateTool("node-tool", "node.exe");
        var script = CreateFile(Path.Combine(_root, "script.mjs"));
        var other = CreateTool("other-tool", "tool.exe");

        Assert.Equal(BaseKeys.Order(StringComparer.OrdinalIgnoreCase), _provider.Prepare(Guid.NewGuid(), node, [script])
            .EnvironmentVariables.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(BaseKeys.Order(StringComparer.OrdinalIgnoreCase), _provider.Prepare(Guid.NewGuid(), other, [])
            .EnvironmentVariables.Keys.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_or_unvalidated_prerequisites_are_unavailable_without_fallback()
    {
        var npmCmd = CreateFile(Path.Combine(_root, "npm-tool", "npm.cmd"));
        var semicolonDirectory = CreateTool("odd;tool", "dotnet.exe");

        Assert.False(_provider.Prepare(Guid.NewGuid(), npmCmd, ["run", "build"]).IsAvailable);
        Assert.False(_provider.Prepare(Guid.NewGuid(), "dotnet.exe", ["build"]).IsAvailable);
        Assert.False(_provider.Prepare(Guid.NewGuid(), Path.Combine(_root, "missing", "dotnet.exe"), ["build"]).IsAvailable);
        Assert.False(_provider.Prepare(Guid.NewGuid(), semicolonDirectory, ["build"]).IsAvailable);

        var notWindows = new WindowsVerificationEnvironmentProvider(
            new WindowsProcessSystemRoot.Inputs(false, static () => Environment.SystemDirectory), _scratch);
        Assert.False(notWindows.Prepare(Guid.NewGuid(), CreateTool("dotnet-tool", "dotnet.exe"), ["build"]).IsAvailable);

        var noSystemDirectory = new WindowsVerificationEnvironmentProvider(
            new WindowsProcessSystemRoot.Inputs(true, static () => null), _scratch);
        Assert.False(noSystemDirectory.Prepare(Guid.NewGuid(), CreateTool("dotnet-tool", "dotnet.exe"), ["build"]).IsAvailable);

        Assert.Equal(WindowsVerificationEnvironmentProvider.ProfileVersion, _provider.Prepare(Guid.NewGuid(), npmCmd, []).ProfileVersion);
    }

    [Fact]
    public void An_execution_never_reuses_scratch_and_only_an_explicit_release_removes_it()
    {
        var dotnet = CreateTool("dotnet-tool", "dotnet.exe");
        var executionId = Guid.NewGuid();

        Assert.True(_provider.Prepare(executionId, dotnet, ["build"]).IsAvailable);
        Assert.False(_provider.Prepare(executionId, dotnet, ["build"]).IsAvailable);

        var owned = Path.Combine(_scratch, executionId.ToString("N"));
        Assert.True(Directory.Exists(owned));
        _provider.ReleaseScratch(executionId);
        Assert.False(Directory.Exists(owned));
    }

    [Theory]
    [InlineData("dotnet.exe", "appdata")]
    [InlineData("dotnet.exe", "nuget-packages")]
    [InlineData("dotnet.exe", "program-files-x86")]
    [InlineData("node.exe", "npm-userconfig")]
    public void A_failure_during_the_owned_setup_is_unavailable_and_removes_the_partial_scratch(string tool, string failingChild)
    {
        // ADR-0072 (review WEP-R5): an I/O failure after the execution's scratch was created and partly populated.
        var executable = CreateTool(tool == "dotnet.exe" ? "dotnet-partial" : "node-partial", tool);
        var npmCli = CreateFile(Path.Combine(Path.GetDirectoryName(executable)!, "node_modules", "npm", "bin", "npm-cli.js"));

        var provider = new WindowsVerificationEnvironmentProvider(WindowsProcessSystemRoot.Inputs.Current, _scratch, path =>
        {
            if (string.Equals(Path.GetFileName(path), failingChild, StringComparison.Ordinal))
            {
                throw new IOException("Scripted partial-setup failure.");
            }
        });
        var executionId = Guid.NewGuid();
        string[] arguments = tool == "node.exe" ? [npmCli, "run", "check"] : ["build"];

        var preparation = provider.Prepare(executionId, executable, arguments);

        Assert.False(preparation.IsAvailable);
        Assert.Equal(WindowsVerificationEnvironmentProvider.ProfileVersion, preparation.ProfileVersion);
        Assert.Empty(preparation.EnvironmentVariables);
        Assert.False(Directory.Exists(Path.Combine(_scratch, executionId.ToString("N"))));
    }

    [Fact]
    public void An_unexpected_failure_during_the_owned_setup_propagates_for_the_supervisor_to_record()
    {
        var dotnet = CreateTool("dotnet-unexpected", "dotnet.exe");
        var provider = new WindowsVerificationEnvironmentProvider(WindowsProcessSystemRoot.Inputs.Current, _scratch, path =>
        {
            if (Path.GetFileName(path) == "profile")
            {
                throw new InvalidOperationException("Scripted unexpected failure.");
            }
        });

        Assert.Throws<InvalidOperationException>(() => provider.Prepare(Guid.NewGuid(), dotnet, ["build"]));
    }

    [Fact]
    public void New_executions_record_version_three_and_the_earlier_names_stay_distinct_historical_values()
    {
        Assert.Equal("windows-verification-v3", WindowsVerificationEnvironmentProvider.ProfileVersion);
        Assert.Equal("windows-verification-v2", WindowsVerificationEnvironmentProvider.HistoricalProfileVersionV2);
        Assert.Equal("windows-verification-v1", WindowsVerificationEnvironmentProvider.HistoricalProfileVersionV1);

        // Unavailable preparations are reported under the current version too; nothing new is ever labelled version one or two.
        Assert.Equal(
            WindowsVerificationEnvironmentProvider.ProfileVersion,
            _provider.Prepare(Guid.NewGuid(), Path.Combine(_root, "missing", "dotnet.exe"), ["build"]).ProfileVersion);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Sentinel, null);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string CreateTool(string directory, string name) => CreateFile(Path.Combine(_root, directory, name));

    private static string CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }
}
