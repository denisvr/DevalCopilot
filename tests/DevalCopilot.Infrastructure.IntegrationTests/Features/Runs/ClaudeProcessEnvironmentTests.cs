using System.ComponentModel;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// The one environment allowlist shared by the three Claude <c>--print</c> adapters, proven first on
/// <see cref="ClaudeProcessEnvironment"/> itself and then through each adapter's real control flow via
/// its internal controlled-input constructor. Every profile value and system directory is fabricated:
/// no ambient variable is read or mutated, no real system directory is consulted, and the process
/// adapter is a non-launching fake, so no Claude CLI, Node runtime or account is ever reached.
/// </summary>
public sealed class ClaudeProcessEnvironmentTests : IDisposable
{
    private const string SensitiveMarker = "SENSITIVE-CLAUDE-ENVIRONMENT-MARKER-6e03";
    private const string FabricatedProfile = @"C:\fabricated-claude-profile-for-tests";
    private const string FabricatedConfig = @"C:\fabricated-claude-config-for-tests";
    private const string FabricatedSystemDirectory = @"C:\FabricatedOs\System32";
    private const string FabricatedSystemRoot = @"C:\FabricatedOs";
    private const string ManifestText = "{\"objective\":\"bounded manifest\"}";

    private static readonly Role[] AllRoles = [Role.CriticalReview, Role.Implementation, Role.ReviewCorrection];

    private readonly string _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-claude-env-artifacts-{Guid.NewGuid():N}");
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-claude-env-workspace-{Guid.NewGuid():N}");
    private readonly FilesystemArtifactStore _artifactStore;

    public ClaudeProcessEnvironmentTests()
    {
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        Directory.CreateDirectory(_workspacePath);
    }

    public enum Role
    {
        CriticalReview,
        Implementation,
        ReviewCorrection,
    }

    public void Dispose()
    {
        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }

        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
    }

    [WindowsPlatformFact]
    public void The_windows_map_is_exactly_the_two_profile_entries_plus_the_derived_SystemRoot()
    {
        var reads = new List<string>();

        var built = ClaudeProcessEnvironment.TryBuild(Inputs(FabricatedProfile, FabricatedConfig, FabricatedSystemDirectory, reads), out var environment);

        Assert.True(built);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["USERPROFILE"] = FabricatedProfile,
                ["CLAUDE_CONFIG_DIR"] = FabricatedConfig,
                ["SystemRoot"] = FabricatedSystemRoot,
            },
            environment);
        Assert.Equal(["USERPROFILE", "CLAUDE_CONFIG_DIR"], reads);
    }

    [Fact]
    public void A_non_windows_map_is_the_former_profile_map_and_never_evaluates_the_system_directory_reader()
    {
        var evaluations = 0;
        var inputs = new ClaudeProcessEnvironmentInputs(
            Variables(FabricatedProfile, FabricatedConfig),
            new(false, () =>
            {
                evaluations++;
                throw new InvalidOperationException(SensitiveMarker);
            }));

        var built = ClaudeProcessEnvironment.TryBuild(inputs, out var environment);

        Assert.True(built);
        Assert.Equal(0, evaluations);
        Assert.Equal(
            new Dictionary<string, string> { ["USERPROFILE"] = FabricatedProfile, ["CLAUDE_CONFIG_DIR"] = FabricatedConfig },
            environment);
    }

    [Theory]
    [InlineData(null, null, new string[0])]
    [InlineData("", "", new string[0])]
    [InlineData("  ", null, new[] { "USERPROFILE" })]
    [InlineData(null, " \t", new[] { "CLAUDE_CONFIG_DIR" })]
    [InlineData(" ", "", new[] { "USERPROFILE" })]
    public void Null_and_empty_profile_values_are_omitted_and_whitespace_values_are_forwarded_verbatim(
        string? userProfile, string? claudeConfig, string[] expectedKeys)
    {
        var built = ClaudeProcessEnvironment.TryBuild(
            new ClaudeProcessEnvironmentInputs(Variables(userProfile, claudeConfig), new(false, null)), out var environment);

        Assert.True(built);
        Assert.NotNull(environment);
        Assert.Equal(expectedKeys.Order(StringComparer.Ordinal), environment.Keys.Order(StringComparer.Ordinal));
        if (environment.TryGetValue("USERPROFILE", out var profileValue))
        {
            Assert.Equal(userProfile, profileValue);
        }

        if (environment.TryGetValue("CLAUDE_CONFIG_DIR", out var configValue))
        {
            Assert.Equal(claudeConfig, configValue);
        }
    }

    [WindowsPlatformFact]
    public void Windows_whitespace_profile_values_are_kept_beside_SystemRoot()
    {
        var built = ClaudeProcessEnvironment.TryBuild(Inputs("  ", null, FabricatedSystemDirectory), out var environment);

        Assert.True(built);
        Assert.Equal(new Dictionary<string, string> { ["USERPROFILE"] = "  ", ["SystemRoot"] = FabricatedSystemRoot }, environment);
    }

    [WindowsPlatformFact]
    public void An_unusable_windows_system_directory_is_refused_with_no_map()
    {
        foreach (var systemDirectory in UnusableSystemDirectories())
        {
            var built = ClaudeProcessEnvironment.TryBuild(Inputs(FabricatedProfile, FabricatedConfig, systemDirectory), out var environment);

            Assert.False(built);
            Assert.Null(environment);
        }
    }

    [Theory]
    [InlineData(nameof(InvalidOperationException))]
    [InlineData(nameof(IOException))]
    [InlineData(nameof(UnauthorizedAccessException))]
    [InlineData(nameof(ArgumentException))]
    [InlineData(nameof(NotSupportedException))]
    [InlineData(nameof(Win32Exception))]
    public void A_throwing_or_unconfigured_windows_reader_is_refused_without_surfacing_the_failure(string failureType)
    {
        var failure = Failure(failureType);

        Assert.False(ClaudeProcessEnvironment.TryBuild(
            new ClaudeProcessEnvironmentInputs(Variables(FabricatedProfile, null), new(true, () => throw failure)), out var thrown));
        Assert.Null(thrown);
        Assert.False(ClaudeProcessEnvironment.TryBuild(
            new ClaudeProcessEnvironmentInputs(Variables(FabricatedProfile, null), new(true, null)), out var unconfigured));
        Assert.Null(unconfigured);
    }

    [Fact]
    public void Cancellation_from_the_system_directory_reader_propagates()
    {
        Assert.Throws<OperationCanceledException>(() => ClaudeProcessEnvironment.TryBuild(
            new ClaudeProcessEnvironmentInputs(Variables(FabricatedProfile, null), new(true, () => throw new OperationCanceledException())),
            out _));
    }

    [WindowsPlatformFact]
    public async Task Every_windows_print_adapter_dispatches_exactly_the_profile_entries_plus_SystemRoot_with_its_contract_intact()
    {
        foreach (var role in AllRoles)
        {
            var reads = new List<string>();
            var invocation = await InvokeAsync(role, Inputs(FabricatedProfile, FabricatedConfig, FabricatedSystemDirectory, reads));

            Assert.False(invocation.Failed);
            var request = Assert.Single(invocation.Process.Requests);
            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["USERPROFILE"] = FabricatedProfile,
                    ["CLAUDE_CONFIG_DIR"] = FabricatedConfig,
                    ["SystemRoot"] = FabricatedSystemRoot,
                },
                request.EnvironmentVariables.ToDictionary(pair => pair.Key, pair => pair.Value));
            Assert.Equal(["USERPROFILE", "CLAUDE_CONFIG_DIR"], reads);
            AssertPrintContract(role, invocation, request);
            AssertNeverContains(invocation.ResultText, FabricatedProfile, FabricatedConfig, "FabricatedOs");
        }
    }

    [Fact]
    public async Task Every_non_windows_print_adapter_keeps_the_former_map_and_never_evaluates_the_reader()
    {
        foreach (var role in AllRoles)
        {
            var evaluations = 0;
            var inputs = new ClaudeProcessEnvironmentInputs(
                Variables(FabricatedProfile, "  "),
                new(false, () =>
                {
                    evaluations++;
                    throw new InvalidOperationException(SensitiveMarker);
                }));

            var invocation = await InvokeAsync(role, inputs);

            Assert.False(invocation.Failed);
            Assert.Equal(0, evaluations);
            Assert.Equal(
                new Dictionary<string, string> { ["USERPROFILE"] = FabricatedProfile, ["CLAUDE_CONFIG_DIR"] = "  " },
                Assert.Single(invocation.Process.Requests).EnvironmentVariables.ToDictionary(pair => pair.Key, pair => pair.Value));
        }
    }

    [WindowsPlatformFact]
    public async Task Every_windows_print_adapter_omits_null_and_empty_profile_values()
    {
        foreach (var role in AllRoles)
        {
            var invocation = await InvokeAsync(role, Inputs(null, string.Empty, FabricatedSystemDirectory));

            Assert.False(invocation.Failed);
            var entry = Assert.Single(Assert.Single(invocation.Process.Requests).EnvironmentVariables);
            Assert.Equal("SystemRoot", entry.Key);
            Assert.Equal(FabricatedSystemRoot, entry.Value);
        }
    }

    [WindowsPlatformFact]
    public async Task An_unusable_windows_system_directory_fails_every_print_adapter_before_any_manifest_sink_or_process_interaction()
    {
        foreach (var role in AllRoles)
        {
            foreach (var systemDirectory in UnusableSystemDirectories())
            {
                var invocation = await InvokeAsync(role, Inputs(FabricatedProfile, FabricatedConfig, systemDirectory));

                AssertRefusedBeforeAnyInteraction(invocation);
                AssertNeverContains(invocation.ResultText, FabricatedProfile, FabricatedConfig);
            }
        }
    }

    [Theory]
    [InlineData(nameof(InvalidOperationException))]
    [InlineData(nameof(IOException))]
    [InlineData(nameof(Win32Exception))]
    public async Task A_throwing_windows_reader_fails_every_print_adapter_before_any_interaction_without_leaking(string failureType)
    {
        foreach (var role in AllRoles)
        {
            var failure = Failure(failureType);
            var invocation = await InvokeAsync(
                role, new ClaudeProcessEnvironmentInputs(Variables(FabricatedProfile, FabricatedConfig), new(true, () => throw failure)));

            AssertRefusedBeforeAnyInteraction(invocation);
            AssertNeverContains(invocation.ResultText, SensitiveMarker, FabricatedProfile, FabricatedConfig);
        }
    }

    [Fact]
    public async Task Cancellation_from_the_reader_propagates_from_every_print_adapter_before_any_interaction()
    {
        foreach (var role in AllRoles)
        {
            var process = new RecordingProcessExecutionAdapter();
            var store = new CountingArtifactStore(_artifactStore);
            var inputs = new ClaudeProcessEnvironmentInputs(
                Variables(FabricatedProfile, null), new(true, () => throw new OperationCanceledException()));

            await Assert.ThrowsAsync<OperationCanceledException>(() => InvokeAsync(role, inputs, process, store));

            Assert.Empty(process.Requests);
            Assert.Equal(0, store.SealedReads);
            Assert.Equal(0, store.PartialPathRequests);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(FabricatedSystemDirectory)]
    public async Task An_already_cancelled_valid_target_request_throws_before_any_environment_read_or_interaction(
        string? systemDirectory)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        foreach (var role in AllRoles)
        {
            var profileReads = 0;
            var systemDirectoryReads = 0;
            var process = new RecordingProcessExecutionAdapter();
            var store = new CountingArtifactStore(_artifactStore);
            // A null system directory would otherwise refuse as an ordinary failure on Windows inputs.
            var inputs = new ClaudeProcessEnvironmentInputs(
                _ =>
                {
                    profileReads++;
                    return FabricatedProfile;
                },
                new(true, () =>
                {
                    systemDirectoryReads++;
                    return systemDirectory;
                }));

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => InvokeAsync(role, inputs, process, store, cancellationToken: cancellation.Token));

            Assert.Equal(0, profileReads);
            Assert.Equal(0, systemDirectoryReads);
            Assert.Equal(0, store.SealedReads);
            Assert.Equal(0, store.PartialPathRequests);
            Assert.Empty(process.Requests);
        }
    }

    [Fact]
    public async Task An_unacceptable_launch_target_keeps_precedence_over_an_already_cancelled_request()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        foreach (var role in AllRoles)
        {
            var evaluations = 0;
            var inputs = new ClaudeProcessEnvironmentInputs(
                _ =>
                {
                    evaluations++;
                    return FabricatedProfile;
                },
                new(true, () =>
                {
                    evaluations++;
                    return FabricatedSystemDirectory;
                }));
            var process = new RecordingProcessExecutionAdapter();
            var store = new CountingArtifactStore(_artifactStore);

            var invocation = await InvokeAsync(
                role, inputs, process, store, Path.Combine(_workspacePath, "missing-claude.exe"), cancellation.Token);

            AssertRefusedBeforeAnyInteraction(invocation);
            Assert.Equal(0, evaluations);
        }
    }

    [WindowsPlatformFact]
    public async Task Process_cancellation_still_propagates_from_every_windows_print_adapter()
    {
        foreach (var role in AllRoles)
        {
            var process = new RecordingProcessExecutionAdapter { ThrowOnExecute = new OperationCanceledException() };

            await Assert.ThrowsAsync<OperationCanceledException>(() => InvokeAsync(
                role, Inputs(FabricatedProfile, null, FabricatedSystemDirectory), process, new CountingArtifactStore(_artifactStore)));

            Assert.Equal(FabricatedSystemRoot, Assert.Single(process.Requests).EnvironmentVariables["SystemRoot"]);
        }
    }

    [Fact]
    public async Task An_unacceptable_launch_target_still_fails_first_without_evaluating_any_environment_input()
    {
        foreach (var role in AllRoles)
        {
            var reads = 0;
            var evaluations = 0;
            var inputs = new ClaudeProcessEnvironmentInputs(
                _ =>
                {
                    reads++;
                    return FabricatedProfile;
                },
                new(true, () =>
                {
                    evaluations++;
                    return FabricatedSystemDirectory;
                }));

            var invocation = await InvokeAsync(role, inputs, launchExecutablePath: Path.Combine(_workspacePath, "missing-claude.exe"));

            AssertRefusedBeforeAnyInteraction(invocation);
            Assert.Equal(0, reads);
            Assert.Equal(0, evaluations);
        }
    }

    private static IEnumerable<string?> UnusableSystemDirectories() =>
    [
        null,
        string.Empty,
        "   ",
        @"relative\System32",
        @"C:\",
        @"C:\Windows",
        @"C:\Windows\..\Windows\System32",
        @"C:\Windows\.\System32",
        @"C:\Wind|ows\System32",
        @"C:\Win" + (char)0 + @"dows\System32",
    ];

    private static Exception Failure(string failureType) => failureType switch
    {
        nameof(InvalidOperationException) => new InvalidOperationException(SensitiveMarker),
        nameof(IOException) => new IOException(SensitiveMarker),
        nameof(UnauthorizedAccessException) => new UnauthorizedAccessException(SensitiveMarker),
        nameof(ArgumentException) => new ArgumentException(SensitiveMarker),
        nameof(NotSupportedException) => new NotSupportedException(SensitiveMarker),
        _ => new Win32Exception(5, SensitiveMarker),
    };

    private static ClaudeProcessEnvironmentInputs Inputs(
        string? userProfile, string? claudeConfig, string? systemDirectory, List<string>? reads = null) =>
        new(Variables(userProfile, claudeConfig, reads), new(true, () => systemDirectory));

    private static Func<string, string?> Variables(string? userProfile, string? claudeConfig, List<string>? reads = null) =>
        name =>
        {
            reads?.Add(name);
            return name switch
            {
                "USERPROFILE" => userProfile,
                "CLAUDE_CONFIG_DIR" => claudeConfig,
                _ => throw new InvalidOperationException("Only the two named profile variables may ever be read."),
            };
        };

    private static void AssertRefusedBeforeAnyInteraction(Invocation invocation)
    {
        Assert.True(invocation.Failed);
        Assert.False(invocation.HasProcessEvidence);
        Assert.Empty(invocation.Process.Requests);
        Assert.Equal(0, invocation.Store.SealedReads);
        Assert.Equal(0, invocation.Store.PartialPathRequests);
    }

    private static void AssertNeverContains(string text, params string[] values)
    {
        foreach (var value in values)
        {
            Assert.DoesNotContain(value, text, StringComparison.Ordinal);
        }
    }

    private static void AssertPrintContract(Role role, Invocation invocation, ProcessExecutionRequest request)
    {
        var arguments = request.Arguments.ToList();
        Assert.Equal(["--print", "--input-format", "text", "--output-format", "json", "--json-schema"], arguments.Take(6));
        Assert.Equal(
            role == Role.CriticalReview ? string.Empty : "Read,Edit,Write,Glob,Grep", arguments[arguments.IndexOf("--tools") + 1]);
        Assert.Equal(role == Role.CriticalReview ? "plan" : "acceptEdits", arguments[arguments.IndexOf("--permission-mode") + 1]);
        string[] isolationFlags =
            ["--safe-mode", "--restricted", "--disable-slash-commands", "--no-chrome", "--strict-mcp-config", "--no-session-persistence"];
        foreach (var flag in isolationFlags)
        {
            Assert.Single(arguments, argument => argument == flag);
        }

        Assert.Equal("none", arguments[arguments.IndexOf("--permission-prompts") + 1]);
        Assert.Equal(invocation.LaunchExecutablePath, request.ExecutablePath);
        Assert.Equal(invocation.WorkspacePath, request.WorkingDirectory);
        Assert.Equal(invocation.WorkspacePath, request.ApprovedRoot);
        Assert.Equal(ManifestText, Encoding.UTF8.GetString(request.StandardInput!));
        Assert.Equal(TimeSpan.FromSeconds(30), request.Timeout);
        Assert.Equal(65536, request.MaxBytesPerStream);
        Assert.Equal(131072, request.MaxTotalCapturedBytes);
        Assert.Equal(1, invocation.Store.SealedReads);
    }

    private Task<Invocation> InvokeAsync(
        Role role, ClaudeProcessEnvironmentInputs inputs, string? launchExecutablePath = null) =>
        InvokeAsync(role, inputs, new RecordingProcessExecutionAdapter(), new CountingArtifactStore(_artifactStore), launchExecutablePath);

    private async Task<Invocation> InvokeAsync(
        Role role,
        ClaudeProcessEnvironmentInputs inputs,
        RecordingProcessExecutionAdapter process,
        CountingArtifactStore store,
        string? launchExecutablePath = null,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        // A sealed manifest and an existing launch file are seeded for every case, so a closed failure
        // can only be explained by the environment rule (or the deliberately missing launch file).
        var partialPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, Encoding.UTF8.GetBytes(ManifestText));
        var manifest = await _artifactStore.SealAsync(runId, attemptId, ArtifactPurpose.AgentContextManifest, CancellationToken.None);
        Assert.NotNull(manifest);

        var executablePath = launchExecutablePath;
        if (executablePath is null)
        {
            executablePath = Path.Combine(_workspacePath, $"fake-claude-{Guid.NewGuid():N}.exe");
            await File.WriteAllTextAsync(executablePath, string.Empty);
        }

        bool failed;
        bool hasEvidence;
        string resultText;
        switch (role)
        {
            case Role.CriticalReview:
                {
                    var result = await new ClaudeCriticalReviewAdapter(process, store, inputs).InvokeAsync(
                        new CriticalReviewInvocationRequest(
                            runId, attemptId, _workspacePath, manifest!.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                            executablePath, TimeSpan.FromSeconds(30), 65536, 131072),
                        cancellationToken);
                    failed = result.Outcome == CriticalReviewInvocationOutcome.Failed;
                    hasEvidence = result.ProcessEvidence is not null;
                    resultText = result.ToString();
                    break;
                }

            case Role.Implementation:
                {
                    var result = await new ClaudeImplementationAdapter(process, store, inputs).InvokeAsync(
                        new ImplementationInvocationRequest(
                            runId, attemptId, _workspacePath, manifest!.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                            executablePath, TimeSpan.FromSeconds(30), 65536, 131072),
                        cancellationToken);
                    failed = result.Outcome == ImplementationInvocationOutcome.Failed;
                    hasEvidence = result.ProcessEvidence is not null;
                    resultText = result.ToString();
                    break;
                }

            default:
                {
                    var result = await new ClaudeReviewCorrectionAdapter(process, store, inputs).InvokeAsync(
                        new ReviewCorrectionInvocationRequest(
                            runId, attemptId, _workspacePath, manifest!.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash,
                            executablePath, TimeSpan.FromSeconds(30), 65536, 131072),
                        cancellationToken);
                    failed = result.Outcome == ImplementationInvocationOutcome.Failed;
                    hasEvidence = result.ProcessEvidence is not null;
                    resultText = result.ToString();
                    break;
                }
        }

        if (failed && !hasEvidence)
        {
            foreach (var purpose in new[] { ArtifactPurpose.AgentFinalResponse, ArtifactPurpose.AgentStandardOutput, ArtifactPurpose.AgentStandardError })
            {
                Assert.False(File.Exists(_artifactStore.GetPartialPath(runId, attemptId, purpose)));
            }
        }

        return new Invocation(failed, hasEvidence, resultText, process, store, executablePath, _workspacePath);
    }

    private sealed record Invocation(
        bool Failed,
        bool HasProcessEvidence,
        string ResultText,
        RecordingProcessExecutionAdapter Process,
        CountingArtifactStore Store,
        string LaunchExecutablePath,
        string WorkspacePath);

    private sealed class RecordingProcessExecutionAdapter : IProcessExecutionAdapter
    {
        private readonly List<ProcessExecutionRequest> _requests = [];

        public IReadOnlyList<ProcessExecutionRequest> Requests => _requests;

        public Exception? ThrowOnExecute { get; init; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            if (ThrowOnExecute is { } exception)
            {
                throw exception;
            }

            return Task.FromResult(new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = JsonSerializer.Serialize(new { is_error = false, result = "{\"ok\":true}", session_id = (string?)null }),
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.Zero,
            });
        }
    }

    /// <summary>Delegates to the real filesystem store while counting the two adapter interactions the
    /// environment refusal must precede: the sealed-manifest read and every sink-path request.</summary>
    private sealed class CountingArtifactStore(IArtifactStore inner) : IArtifactStore
    {
        public int SealedReads { get; private set; }

        public int PartialPathRequests { get; private set; }

        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose)
        {
            PartialPathRequests++;
            return inner.GetPartialPath(runId, attemptId, purpose);
        }

        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            inner.GetSealedRelativePath(runId, attemptId, purpose);

        public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            inner.SealAsync(runId, attemptId, purpose, cancellationToken);

        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.HasSealedFile(runId, attemptId, purpose);

        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => inner.HasPartialFile(runId, attemptId, purpose);

        public Task<SealedOutputFile?> DescribeSealedFileAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            inner.DescribeSealedFileAsync(runId, attemptId, purpose, cancellationToken);

        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            inner.DeleteOrphanedPartialFile(runId, attemptId, purpose);

        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            inner.DeleteOrphanedSealedFile(runId, attemptId, purpose);

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
            inner.ReadPartialAsync(runId, attemptId, purpose, fromOffset, maxBytes, cancellationToken);

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath,
            long expectedByteLength,
            string expectedContentHash,
            long fromOffset,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            SealedReads++;
            return inner.VerifyAndReadSealedAsync(
                relativeStoragePath, expectedByteLength, expectedContentHash, fromOffset, maxBytes, cancellationToken);
        }
    }
}
