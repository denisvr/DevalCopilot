using System.ComponentModel;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Exercises the launch preparation of the one shared <see cref="CodexAppServerSession"/> through
/// its injectable-input overload: the Windows script-hosted <c>SystemRoot</c> rule, the unchanged
/// USERPROFILE/CODEX_HOME inclusion semantics, the argument and redirection contract, and the
/// refuse-before-scratch-and-start control flow. Every case uses fabricated profile values and
/// system directories and a capturing process-start callback that records the prepared
/// <see cref="System.Diagnostics.ProcessStartInfo"/> and throws a handled start failure, so no
/// child process is ever launched and no ambient variable is read or mutated. The complete
/// fixture-driven adapter classes separately exercise the real duplex transport.
/// </summary>
public sealed class CodexAppServerSessionEnvironmentTests : IDisposable
{
    private const string SensitiveMarker = "SENSITIVE-APP-SERVER-MARKER-5e13";
    private const string FabricatedProfile = @"C:\fabricated-profile-for-tests";
    private const string FabricatedCodexHome = @"C:\fabricated-codex-home-for-tests";
    private const string FabricatedSystemDirectory = @"C:\FabricatedOs\System32";
    private const string FabricatedSystemRoot = @"C:\FabricatedOs";

    private readonly string _root;
    private readonly string _executablePath;
    private readonly string _scriptPath;
    private readonly string _scratchDirectory;

    public CodexAppServerSessionEnvironmentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-app-server-env-{Guid.NewGuid():N}");
        _scratchDirectory = Path.Combine(_root, "scratch");
        Directory.CreateDirectory(_scratchDirectory);
        _executablePath = Path.Combine(_root, "fake-codex-or-node.exe");
        _scriptPath = Path.Combine(_root, "fake-codex-cli.js");
        File.WriteAllText(_executablePath, string.Empty);
        File.WriteAllText(_scriptPath, string.Empty);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [WindowsPlatformFact]
    public async Task A_windows_script_launch_gets_SystemRoot_plus_exactly_the_admitted_profile_entries_unchanged()
    {
        var harness = new Harness(this, FabricatedProfile, FabricatedCodexHome, isWindows: true, FabricatedSystemDirectory);

        var result = await harness.RunAsync(_scriptPath);

        Assert.Same(harness.Unknown, result);
        var started = Assert.Single(harness.Started);
        Assert.Equal(
            Env(("USERPROFILE", FabricatedProfile), ("CODEX_HOME", FabricatedCodexHome), ("SystemRoot", FabricatedSystemRoot)),
            started.Environment);
        Assert.Equal(["USERPROFILE", "CODEX_HOME"], harness.VariableReads);
        Assert.Equal(["variables", "system-directory", "scratch", "start"], harness.Order);
        Assert.DoesNotContain(FabricatedProfile, harness.Describe(result), StringComparison.Ordinal);
        Assert.DoesNotContain("FabricatedOs", harness.Describe(result), StringComparison.Ordinal);
    }

    [WindowsOnlyTheory]
    [InlineData("  ", "", new[] { "USERPROFILE" })]
    [InlineData("", "  ", new[] { "CODEX_HOME" })]
    [InlineData(null, null, new string[0])]
    [InlineData("", "", new string[0])]
    [InlineData(FabricatedProfile, null, new[] { "USERPROFILE" })]
    [InlineData(null, FabricatedCodexHome, new[] { "CODEX_HOME" })]
    public async Task Profile_inclusion_semantics_are_unchanged_null_and_empty_omitted_whitespace_kept_verbatim(
        string? userProfile, string? codexHome, string[] expectedProfileKeys)
    {
        var harness = new Harness(this, userProfile, codexHome, isWindows: true, FabricatedSystemDirectory);

        await harness.RunAsync(_scriptPath);

        var started = Assert.Single(harness.Started);
        var expected = new Dictionary<string, string?> { ["SystemRoot"] = FabricatedSystemRoot };
        foreach (var key in expectedProfileKeys)
        {
            expected[key] = key == "USERPROFILE" ? userProfile : codexHome;
        }

        Assert.Equal(expected, started.Environment);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_native_launch_keeps_its_original_map_and_never_evaluates_the_reader(bool isWindows)
    {
        var harness = new Harness(this, FabricatedProfile, FabricatedCodexHome, isWindows, systemDirectory: null)
        {
            ReaderThrows = true,
        };

        var result = await harness.RunAsync(scriptPath: null);

        Assert.Same(harness.Unknown, result);
        Assert.Equal(0, harness.SystemDirectoryReads);
        var started = Assert.Single(harness.Started);
        Assert.Equal(
            Env(("USERPROFILE", FabricatedProfile), ("CODEX_HOME", FabricatedCodexHome)), started.Environment);
        Assert.Equal(_executablePath, started.FileName);
        Assert.Equal(["app-server", "--stdio"], started.Arguments);
    }

    [Fact]
    public async Task A_script_launch_off_windows_keeps_its_original_map_and_never_evaluates_the_reader()
    {
        var harness = new Harness(this, FabricatedProfile, null, isWindows: false, systemDirectory: null) { ReaderThrows = true };

        var result = await harness.RunAsync(_scriptPath);

        Assert.Same(harness.Unknown, result);
        Assert.Equal(0, harness.SystemDirectoryReads);
        var started = Assert.Single(harness.Started);
        var entry = Assert.Single(started.Environment);
        Assert.Equal("USERPROFILE", entry.Key);
        Assert.Equal(FabricatedProfile, entry.Value);
        Assert.Equal([_scriptPath, "app-server", "--stdio"], started.Arguments);
    }

    [WindowsOnlyTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\System32")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\..\Windows\System32")]
    [InlineData(@"C:\Windows\.\System32")]
    [InlineData(@"C:\Wind|ows\System32")]
    public async Task An_unusable_system_directory_refuses_with_the_supplied_unknown_result_before_scratch_and_start(
        string? systemDirectory)
    {
        var harness = new Harness(this, FabricatedProfile, FabricatedCodexHome, isWindows: true, systemDirectory);

        var result = await harness.RunAsync(_scriptPath);

        harness.AssertRefusedBeforeScratchAndStart(result);
    }

    [WindowsPlatformFact]
    public async Task A_control_character_system_directory_refuses_before_scratch_and_start()
    {
        var harness = new Harness(
            this, FabricatedProfile, FabricatedCodexHome, isWindows: true, @"C:\Win" + (char)0 + @"dows\System32");

        var result = await harness.RunAsync(_scriptPath);

        harness.AssertRefusedBeforeScratchAndStart(result);
    }

    [Theory]
    [InlineData(nameof(InvalidOperationException))]
    [InlineData(nameof(IOException))]
    [InlineData(nameof(UnauthorizedAccessException))]
    [InlineData(nameof(Win32Exception))]
    public async Task A_throwing_reader_refuses_without_leaking_its_failure(string failureType)
    {
        var harness = new Harness(this, FabricatedProfile, FabricatedCodexHome, isWindows: true, FabricatedSystemDirectory)
        {
            ReaderFailure = failureType switch
            {
                nameof(InvalidOperationException) => new InvalidOperationException(SensitiveMarker),
                nameof(IOException) => new IOException(SensitiveMarker),
                nameof(UnauthorizedAccessException) => new UnauthorizedAccessException(SensitiveMarker),
                _ => new Win32Exception(5, SensitiveMarker),
            },
        };

        var result = await harness.RunAsync(_scriptPath);

        harness.AssertRefusedBeforeScratchAndStart(result);
        Assert.DoesNotContain(SensitiveMarker, harness.Describe(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_from_the_reader_is_not_swallowed_and_creates_nothing()
    {
        var harness = new Harness(this, FabricatedProfile, FabricatedCodexHome, isWindows: true, FabricatedSystemDirectory)
        {
            ReaderFailure = new OperationCanceledException(),
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => harness.RunAsync(_scriptPath));

        Assert.Equal(0, harness.ScratchCalls);
        Assert.Empty(harness.Started);
        Assert.Equal(0, harness.ExchangeCalls);
    }

    [Fact]
    public async Task An_already_cancelled_call_throws_before_any_input_is_evaluated()
    {
        var harness = new Harness(this, FabricatedProfile, FabricatedCodexHome, isWindows: true, FabricatedSystemDirectory);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.RunAsync(_scriptPath, cancellationToken: cancellation.Token));

        Assert.Empty(harness.Order);
        Assert.Equal(0, harness.ExchangeCalls);
    }

    [Theory]
    [InlineData("missing-script")]
    [InlineData("relative-script")]
    [InlineData("missing-executable")]
    public async Task A_launch_target_that_fails_revalidation_returns_unknown_before_the_reader_scratch_or_start(string defect)
    {
        var harness = new Harness(this, FabricatedProfile, FabricatedCodexHome, isWindows: true, FabricatedSystemDirectory);
        var executable = defect == "missing-executable" ? Path.Combine(_root, "absent.exe") : _executablePath;
        var script = defect switch
        {
            "missing-script" => Path.Combine(_root, "absent.js"),
            "relative-script" => "relative.js",
            _ => _scriptPath,
        };

        var result = await harness.RunAsync(script, executablePath: executable);

        harness.AssertRefusedBeforeScratchAndStart(result);
        Assert.Equal(0, harness.SystemDirectoryReads);
        Assert.Empty(harness.VariableReads);
        Assert.Empty(harness.Order);
    }

    [WindowsPlatformFact]
    public async Task A_scratch_failure_after_a_derived_SystemRoot_still_returns_unknown_without_starting()
    {
        var harness = new Harness(this, FabricatedProfile, FabricatedCodexHome, isWindows: true, FabricatedSystemDirectory)
        {
            ScratchFailure = new IOException(SensitiveMarker),
        };

        var result = await harness.RunAsync(_scriptPath);

        Assert.Same(harness.Unknown, result);
        Assert.Equal(1, harness.ScratchCalls);
        Assert.Empty(harness.Started);
        Assert.Equal(0, harness.ExchangeCalls);
        Assert.DoesNotContain(SensitiveMarker, harness.Describe(result), StringComparison.Ordinal);
    }

    [WindowsPlatformFact]
    public async Task The_launch_contract_is_otherwise_unchanged_for_a_windows_script_launch()
    {
        var harness = new Harness(this, FabricatedProfile, null, isWindows: true, FabricatedSystemDirectory);

        await harness.RunAsync(_scriptPath);

        var started = Assert.Single(harness.Started);
        Assert.Equal(_executablePath, started.FileName);
        Assert.Equal(_scratchDirectory, started.WorkingDirectory);
        Assert.Equal([_scriptPath, "app-server", "--stdio"], started.Arguments);
        Assert.False(started.UseShellExecute);
        Assert.True(started.RedirectStandardInput);
        Assert.True(started.RedirectStandardOutput);
        Assert.True(started.RedirectStandardError);
        Assert.True(started.CreateNoWindow);
    }

    [Theory]
    [InlineData(nameof(InvalidOperationException))]
    [InlineData(nameof(IOException))]
    [InlineData(nameof(Win32Exception))]
    public async Task A_handled_start_failure_returns_unknown_without_surfacing_detail(string failureType)
    {
        var harness = new Harness(this, FabricatedProfile, null, isWindows: false, systemDirectory: null)
        {
            StartFailure = failureType switch
            {
                nameof(InvalidOperationException) => new InvalidOperationException(SensitiveMarker),
                nameof(IOException) => new IOException(SensitiveMarker),
                _ => new Win32Exception(5, SensitiveMarker),
            },
        };

        var result = await harness.RunAsync(_scriptPath);

        Assert.Same(harness.Unknown, result);
        Assert.Equal(0, harness.ExchangeCalls);
        Assert.DoesNotContain(SensitiveMarker, harness.Describe(result), StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> Env(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => (string?)entry.Value);

    private sealed record Observation(string Name);

    private sealed record StartedProcess(
        string FileName,
        string WorkingDirectory,
        string[] Arguments,
        Dictionary<string, string?> Environment,
        bool UseShellExecute,
        bool RedirectStandardInput,
        bool RedirectStandardOutput,
        bool RedirectStandardError,
        bool CreateNoWindow);

    /// <summary>Wires fabricated launch inputs to recording callbacks. The variable reader allows
    /// only the two admitted profile names; the process-start callback records the prepared start
    /// info and then throws a handled failure, so nothing is ever launched.</summary>
    private sealed class Harness
    {
        private readonly CodexAppServerSessionEnvironmentTests _owner;
        private readonly string? _userProfile;
        private readonly string? _codexHome;
        private readonly bool _isWindows;
        private readonly string? _systemDirectory;

        public Harness(
            CodexAppServerSessionEnvironmentTests owner, string? userProfile, string? codexHome, bool isWindows, string? systemDirectory)
        {
            _owner = owner;
            _userProfile = userProfile;
            _codexHome = codexHome;
            _isWindows = isWindows;
            _systemDirectory = systemDirectory;
        }

        public Observation Unknown { get; } = new("unknown");

        public bool ReaderThrows { get; init; }

        public Exception? ReaderFailure { get; init; }

        public Exception? ScratchFailure { get; init; }

        public Exception? StartFailure { get; init; }

        public List<string> Order { get; } = [];

        public List<string> VariableReads { get; } = [];

        public List<StartedProcess> Started { get; } = [];

        public int SystemDirectoryReads { get; private set; }

        public int ScratchCalls { get; private set; }

        public int ExchangeCalls { get; private set; }

        public string Describe(Observation result) => result.ToString();

        public Task<Observation> RunAsync(string? scriptPath, string? executablePath = null, CancellationToken cancellationToken = default) =>
            CodexAppServerSession.RunAsync(
                executablePath ?? _owner._executablePath,
                scriptPath,
                TimeSpan.FromSeconds(5),
                4096,
                1024,
                Unknown,
                (_, _) =>
                {
                    ExchangeCalls++;
                    return Task.FromResult(new Observation("exchanged"));
                },
                new CodexAppServerSession.LaunchInputs(ReadVariable, new(_isWindows, ReadSystemDirectory), EnsureScratch, StartProcess),
                cancellationToken);

        public void AssertRefusedBeforeScratchAndStart(Observation result)
        {
            Assert.Same(Unknown, result);
            Assert.Equal(0, ScratchCalls);
            Assert.Empty(Started);
            Assert.Equal(0, ExchangeCalls);
        }

        private string? ReadVariable(string name)
        {
            Assert.Contains(name, new[] { "USERPROFILE", "CODEX_HOME" });
            if (VariableReads.Count == 0)
            {
                Order.Add("variables");
            }

            VariableReads.Add(name);
            return name == "USERPROFILE" ? _userProfile : _codexHome;
        }

        private string? ReadSystemDirectory()
        {
            SystemDirectoryReads++;
            Order.Add("system-directory");
            if (ReaderThrows)
            {
                throw new InvalidOperationException(SensitiveMarker);
            }

            if (ReaderFailure is { } failure)
            {
                throw failure;
            }

            return _systemDirectory;
        }

        private string EnsureScratch()
        {
            ScratchCalls++;
            Order.Add("scratch");
            if (ScratchFailure is { } failure)
            {
                throw failure;
            }

            return _owner._scratchDirectory;
        }

        private void StartProcess(System.Diagnostics.Process process)
        {
            Order.Add("start");
            var info = process.StartInfo;
            Started.Add(new StartedProcess(
                info.FileName,
                info.WorkingDirectory,
                [.. info.ArgumentList],
                info.Environment.ToDictionary(pair => pair.Key, pair => pair.Value),
                info.UseShellExecute,
                info.RedirectStandardInput,
                info.RedirectStandardOutput,
                info.RedirectStandardError,
                info.CreateNoWindow));
            throw StartFailure ?? new InvalidOperationException(SensitiveMarker);
        }
    }
}
