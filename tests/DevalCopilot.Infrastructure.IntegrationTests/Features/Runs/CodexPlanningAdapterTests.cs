using System.Diagnostics;
using System.Text;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// Exercises <see cref="CodexPlanningAdapter"/>'s own contract construction and control flow — CLI
/// argument shape, stdin-only prompt delivery, manifest/scratch containment, reparse-point
/// rejection, launch-target revalidation, environment allowlisting, and closed failure handling.
/// Uses a hand-rolled fake <see cref="IProcessExecutionAdapter"/> (mirroring the
/// <c>FakeArtifactStore</c> pattern already used for this slice's Application-layer handler tests)
/// rather than a real OS process: the real-process boundary itself is already covered by
/// <see cref="Processes.ChildProcessExecutionAdapterTests"/>, and this adapter must never actually
/// launch a real Codex/node binary in a test. The real <see cref="FilesystemArtifactStore"/> is
/// used (rooted at a disposable temp directory) because manifest verification and containment are
/// genuine filesystem behavior this adapter depends on.
/// </summary>
public sealed class CodexPlanningAdapterTests : IDisposable
{
    private readonly string _artifactRoot;
    private readonly FilesystemArtifactStore _artifactStore;
    private readonly string _workspacePath;

    public CodexPlanningAdapterTests()
    {
        _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-codex-adapter-artifacts-{Guid.NewGuid():N}");
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        _workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-codex-adapter-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspacePath);
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

    [Fact]
    public async Task A_direct_executable_launch_target_produces_the_exact_contracted_argument_list()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-direct.exe");
        var scratchDirectory = AgentInvocationScratchDirectory.EnsureExists(runId, attemptId);

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, LaunchScriptPath: null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CodexPlanningInvocationOutcome.Exited, result.Outcome);
        Assert.NotNull(fake.CapturedRequest);
        var expectedSchemaPath = Path.Combine(scratchDirectory, "schema.json");
        var expectedResultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        string[] expectedArguments =
        [
            "exec",
            "--json",
            "--output-schema", expectedSchemaPath,
            "--output-last-message", expectedResultPath,
            "--sandbox", "read-only",
            "--cd", _workspacePath,
            "--ephemeral",
            "--ignore-user-config",
            "-",
        ];
        Assert.Equal(expectedArguments, fake.CapturedRequest!.Arguments.ToArray());
        Assert.Equal(executablePath, fake.CapturedRequest.ExecutablePath);
        Assert.Equal(_workspacePath, fake.CapturedRequest.WorkingDirectory);
        Assert.Equal(_workspacePath, fake.CapturedRequest.ApprovedRoot);
    }

    [Fact]
    public async Task A_node_script_launch_target_prepends_the_script_path_and_keeps_the_same_flag_contract()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var nodeExecutablePath = CreateLaunchFile("fake-node.exe");
        var scriptPath = CreateLaunchFile("fake-codex-cli.js");
        var scratchDirectory = AgentInvocationScratchDirectory.EnsureExists(runId, attemptId);

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            nodeExecutablePath, scriptPath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CodexPlanningInvocationOutcome.Exited, result.Outcome);
        Assert.NotNull(fake.CapturedRequest);
        var expectedSchemaPath = Path.Combine(scratchDirectory, "schema.json");
        var expectedResultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        string[] expectedArguments =
        [
            scriptPath,
            "exec",
            "--json",
            "--output-schema", expectedSchemaPath,
            "--output-last-message", expectedResultPath,
            "--sandbox", "read-only",
            "--cd", _workspacePath,
            "--ephemeral",
            "--ignore-user-config",
            "-",
        ];
        Assert.Equal(expectedArguments, fake.CapturedRequest!.Arguments.ToArray());
        Assert.Equal(nodeExecutablePath, fake.CapturedRequest.ExecutablePath);
    }

    [Fact]
    public async Task The_context_manifest_is_supplied_only_via_stdin_and_never_appears_as_a_command_line_argument()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        const string ManifestMarker = "MANIFEST-CONTENT-MUST-ONLY-ARRIVE-VIA-STDIN-6a10f2";
        var manifest = await SeedSealedManifestAsync(runId, attemptId, $$"""{"marker":"{{ManifestMarker}}"}""");
        var executablePath = CreateLaunchFile("fake-codex-stdin.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.NotNull(fake.CapturedRequest);
        Assert.All(fake.CapturedRequest!.Arguments, argument => Assert.DoesNotContain(ManifestMarker, argument));
        Assert.NotNull(fake.CapturedRequest.StandardInput);
        Assert.Equal(manifest.Text, Encoding.UTF8.GetString(fake.CapturedRequest.StandardInput!));
        Assert.Contains(ManifestMarker, manifest.Text);
    }

    [Fact]
    public async Task A_context_manifest_relative_path_that_escapes_the_artifact_root_fails_closed_without_ever_invoking_a_process()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var executablePath = CreateLaunchFile("fake-codex-escape.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, "../../outside-the-artifact-root.txt", 10, "sha256:" + new string('0', 64),
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.CapturedRequest);
    }

    [Fact]
    public async Task An_absolute_context_manifest_path_is_rejected_rather_than_trusted()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var executablePath = CreateLaunchFile("fake-codex-absolute-manifest.exe");
        var outsideFile = Path.Combine(_workspacePath, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "not a real sealed artifact");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, outsideFile, 10, "sha256:" + new string('0', 64),
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.CapturedRequest);
    }

    [WindowsOnlyFact]
    public async Task A_reparse_point_at_the_scratch_directory_location_is_rejected_rather_than_followed()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-reparse.exe");

        var scratchParent = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevalCopilot", "agent-scratch",
            runId.ToString());
        Directory.CreateDirectory(scratchParent);
        var junctionPath = Path.Combine(scratchParent, attemptId.ToString());
        var realTarget = Path.Combine(_workspacePath, "reparse-target");
        Directory.CreateDirectory(realTarget);

        var junctionCreation = TryCreateJunction(junctionPath, realTarget);
        try
        {
            // Directory junction creation needs no elevation on Windows (verified empirically for
            // this suite in PackageEntrypointResolverTests) — a failure here is a genuine
            // environmental problem worth failing loudly over, never a reason to silently treat
            // the security assertion below as satisfied.
            Assert.True(
                junctionCreation.Succeeded,
                $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

            var fake = new FakeProcessExecutionAdapter();
            var adapter = new CodexPlanningAdapter(fake, _artifactStore);
            var request = new CodexPlanningInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
                executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

            var result = await adapter.InvokeAsync(request, CancellationToken.None);

            Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
            Assert.Null(fake.CapturedRequest);
        }
        finally
        {
            // AgentInvocationScratchDirectory.TryDelete's own reparse-point check now makes this
            // safe either way: the adapter's own cleanup (in its outer `finally`) already removes
            // a reparse-point scratch directory non-recursively, so this is normally a no-op; if
            // cleanup somehow did not run, this still removes only the junction link itself, never
            // recursing into its real target.
            if (Directory.Exists(junctionPath))
            {
                Directory.Delete(junctionPath);
            }

            if (Directory.Exists(scratchParent))
            {
                Directory.Delete(scratchParent, recursive: true);
            }
        }
    }

    [WindowsOnlyFact]
    public async Task An_intermediate_ancestor_junction_of_the_scratch_directory_path_causes_rejection_without_ever_invoking_a_process()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-scratch-ancestor.exe");

        var scratchRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevalCopilot", "agent-scratch");
        Directory.CreateDirectory(scratchRoot);
        var runJunctionPath = Path.Combine(scratchRoot, runId.ToString());
        var realTargetDirectory = Path.Combine(_workspacePath, "real-scratch-ancestor-target");
        Directory.CreateDirectory(realTargetDirectory);

        // The external target already contains the EXACT {attemptId} leaf directory the
        // adapter's scratch-path computation would produce — not merely a sibling of it. A prior
        // round's version of this test planted its sentinel as a sibling of the generated attempt
        // directory and explicitly accepted that the attempt directory itself got deleted through
        // the junction, which disguised the very bug this correction targets as a passing test.
        var realAttemptDirectory = Path.Combine(realTargetDirectory, attemptId.ToString());
        Directory.CreateDirectory(realAttemptDirectory);
        const string SentinelContent = "sentinel-scratch-directory-ancestor-must-not-be-touched";
        var sentinelPath = Path.Combine(realAttemptDirectory, "sentinel.txt");
        File.WriteAllText(sentinelPath, SentinelContent);

        // The junction sits at the *runId* segment — an intermediate ancestor of the scratch
        // directory EnsureExists will return (…\agent-scratch\{runId}\{attemptId}) — never at the
        // leaf itself, which the existing leaf-only test above already covers.
        var junctionCreation = TryCreateJunction(runJunctionPath, realTargetDirectory);
        try
        {
            Assert.True(
                junctionCreation.Succeeded,
                $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

            var fake = new FakeProcessExecutionAdapter();
            var adapter = new CodexPlanningAdapter(fake, _artifactStore);
            var request = new CodexPlanningInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
                executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

            var result = await adapter.InvokeAsync(request, CancellationToken.None);

            Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
            Assert.Null(fake.CapturedRequest);
            // The real directory the junction points to — reachable only through the
            // reparse-point ancestor — including the pre-existing {attemptId} directory and its
            // sentinel file, was never touched, read, or deleted by the rejected invocation or
            // its own cleanup path: byte-for-byte identical to what existed before the call.
            Assert.True(Directory.Exists(realTargetDirectory));
            Assert.True(Directory.Exists(realAttemptDirectory));
            Assert.True(File.Exists(sentinelPath));
            Assert.Equal(SentinelContent, File.ReadAllText(sentinelPath));
        }
        finally
        {
            if (Directory.Exists(runJunctionPath))
            {
                Directory.Delete(runJunctionPath);
            }
        }
    }

    [WindowsOnlyFact]
    public async Task An_intermediate_ancestor_junction_of_the_launch_executable_path_causes_rejection_without_ever_invoking_a_process()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");

        var realTargetDirectory = Path.Combine(_workspacePath, "real-launch-executable-target");
        Directory.CreateDirectory(realTargetDirectory);
        const string SentinelContent = "sentinel-launch-executable-ancestor-must-not-be-touched";
        var sentinelPath = Path.Combine(realTargetDirectory, "sentinel.txt");
        File.WriteAllText(sentinelPath, SentinelContent);
        File.WriteAllText(Path.Combine(realTargetDirectory, "fake-codex.exe"), string.Empty);

        var junctionDirectory = Path.Combine(_workspacePath, "launch-executable-junction");
        var junctionCreation = TryCreateJunction(junctionDirectory, realTargetDirectory);
        try
        {
            Assert.True(
                junctionCreation.Succeeded,
                $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

            // The leaf file itself is fully qualified and genuinely exists (via traversal through
            // the junction) — the rejection must come from the *ancestor* segment, not from
            // Path.IsPathFullyQualified or File.Exists failing.
            var executablePath = Path.Combine(junctionDirectory, "fake-codex.exe");
            var fake = new FakeProcessExecutionAdapter();
            var adapter = new CodexPlanningAdapter(fake, _artifactStore);
            var request = new CodexPlanningInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
                executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

            var result = await adapter.InvokeAsync(request, CancellationToken.None);

            Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
            Assert.Null(fake.CapturedRequest);
            Assert.True(File.Exists(sentinelPath));
            Assert.Equal(SentinelContent, File.ReadAllText(sentinelPath));
        }
        finally
        {
            if (Directory.Exists(junctionDirectory))
            {
                Directory.Delete(junctionDirectory);
            }
        }
    }

    [WindowsOnlyFact]
    public async Task An_intermediate_ancestor_junction_of_the_launch_script_path_causes_rejection_without_ever_invoking_a_process()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var nodeExecutablePath = CreateLaunchFile("fake-node-for-script-ancestor.exe");

        var realTargetDirectory = Path.Combine(_workspacePath, "real-launch-script-target");
        Directory.CreateDirectory(realTargetDirectory);
        const string SentinelContent = "sentinel-launch-script-ancestor-must-not-be-touched";
        var sentinelPath = Path.Combine(realTargetDirectory, "sentinel.txt");
        File.WriteAllText(sentinelPath, SentinelContent);
        File.WriteAllText(Path.Combine(realTargetDirectory, "cli.js"), string.Empty);

        var junctionDirectory = Path.Combine(_workspacePath, "launch-script-junction");
        var junctionCreation = TryCreateJunction(junctionDirectory, realTargetDirectory);
        try
        {
            Assert.True(
                junctionCreation.Succeeded,
                $"Directory junction creation failed (exit code {junctionCreation.ExitCode}): {junctionCreation.StandardError}");

            // The node executable itself is an ordinary, reparse-point-free file — proving the
            // rejection is driven specifically by the script path's ancestor, exactly mirroring
            // how the executable-path test above isolates that side of the check.
            var scriptPath = Path.Combine(junctionDirectory, "cli.js");
            var fake = new FakeProcessExecutionAdapter();
            var adapter = new CodexPlanningAdapter(fake, _artifactStore);
            var request = new CodexPlanningInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
                nodeExecutablePath, scriptPath, TimeSpan.FromSeconds(30), 65536, 131072);

            var result = await adapter.InvokeAsync(request, CancellationToken.None);

            Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
            Assert.Null(fake.CapturedRequest);
            Assert.True(File.Exists(sentinelPath));
            Assert.Equal(SentinelContent, File.ReadAllText(sentinelPath));
        }
        finally
        {
            if (Directory.Exists(junctionDirectory))
            {
                Directory.Delete(junctionDirectory);
            }
        }
    }

    [Fact]
    public async Task A_launch_executable_that_no_longer_exists_at_invocation_time_fails_closed_without_re_searching()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var missingExecutablePath = Path.Combine(_workspacePath, $"does-not-exist-{Guid.NewGuid():N}.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            missingExecutablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.CapturedRequest);
    }

    [Fact]
    public async Task A_launch_script_that_no_longer_exists_at_invocation_time_fails_closed()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var nodeExecutablePath = CreateLaunchFile("fake-node-for-missing-script.exe");
        var missingScriptPath = Path.Combine(_workspacePath, $"does-not-exist-{Guid.NewGuid():N}.js");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            nodeExecutablePath, missingScriptPath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.CapturedRequest);
    }

    [Fact]
    public async Task A_launch_executable_path_that_is_not_fully_qualified_fails_closed()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            "codex.exe", null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.CapturedRequest);
    }

    [Fact]
    public async Task Only_the_minimal_non_secret_environment_allowlist_reaches_the_child_never_path()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-env.exe");

        var previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        Environment.SetEnvironmentVariable("CODEX_HOME", @"C:\fake-codex-home-for-tests");
        try
        {
            var fake = new FakeProcessExecutionAdapter();
            var adapter = new CodexPlanningAdapter(fake, _artifactStore);
            var request = new CodexPlanningInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
                executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

            var result = await adapter.InvokeAsync(request, CancellationToken.None);

            Assert.Equal(CodexPlanningInvocationOutcome.Exited, result.Outcome);
            Assert.NotNull(fake.CapturedRequest);
            var environment = fake.CapturedRequest!.EnvironmentVariables;
            Assert.False(environment.ContainsKey("PATH"));
            Assert.Equal(@"C:\fake-codex-home-for-tests", environment["CODEX_HOME"]);

            var expectedUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(expectedUserProfile))
            {
                Assert.Equal(expectedUserProfile, environment["USERPROFILE"]);
                Assert.Equal(2, environment.Count);
            }
            else
            {
                Assert.Single(environment);
            }

            // The result is a closed record of enums/booleans/an optional session id string —
            // nothing environment-shaped can ever be recorded from it into persistence or logs.
            AssertResultNeverContains(result, @"C:\fake-codex-home-for-tests");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
        }
    }

    [Fact]
    public async Task A_non_zero_exit_from_the_provider_produces_a_safe_closed_failed_outcome_preserving_truncation_flags()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-authfail.exe");

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 1,
                StandardOutput = string.Empty,
                StandardOutputTruncated = true,
                StandardError = "not authenticated",
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
        Assert.True(result.StandardOutputTruncated);
        Assert.False(result.StandardErrorTruncated);
        Assert.Null(result.ProviderSessionId);
    }

    [Fact]
    public async Task A_thrown_execution_exception_becomes_a_safe_closed_failed_outcome_never_the_exceptions_own_text()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-throws.exe");

        const string SensitiveDetail = "sensitive-failure-detail-must-not-leak-72fa1c";
        var fake = new FakeProcessExecutionAdapter { ThrowOnExecute = new InvalidOperationException(SensitiveDetail) };
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CodexPlanningInvocationOutcome.Failed, result.Outcome);
        AssertResultNeverContains(result, SensitiveDetail);
    }

    [Fact]
    public async Task A_cancellation_during_process_execution_propagates_rather_than_being_swallowed_as_a_failed_outcome()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-cancel.exe");

        var fake = new FakeProcessExecutionAdapter { ThrowOnExecute = new OperationCanceledException() };
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.InvokeAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task A_session_id_reported_on_a_json_stdout_line_is_surfaced_on_a_successful_exit()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-session.exe");

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = """{"type":"session_meta","session_id":"session-abc-123"}""" + "\n",
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CodexPlanningInvocationOutcome.Exited, result.Outcome);
        Assert.Equal("session-abc-123", result.ProviderSessionId);
    }

    [Fact]
    public async Task The_scratch_directory_is_removed_after_a_successful_invocation()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-codex-cleanup.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new CodexPlanningAdapter(fake, _artifactStore);
        var request = new CodexPlanningInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, null, TimeSpan.FromSeconds(30), 65536, 131072);

        await adapter.InvokeAsync(request, CancellationToken.None);

        var scratchDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevalCopilot", "agent-scratch",
            runId.ToString(), attemptId.ToString());
        Assert.False(Directory.Exists(scratchDirectory));
    }

    private static void AssertResultNeverContains(CodexPlanningInvocationResult result, string forbidden)
    {
        foreach (var property in typeof(CodexPlanningInvocationResult).GetProperties())
        {
            if (property.GetValue(result) is string value)
            {
                Assert.DoesNotContain(forbidden, value);
            }
        }
    }

    private string CreateLaunchFile(string fileName)
    {
        var path = Path.Combine(_workspacePath, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private async Task<SeededManifest> SeedSealedManifestAsync(Guid runId, Guid attemptId, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var partialPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, bytes);

        var sealedFile = await _artifactStore.SealAsync(runId, attemptId, ArtifactPurpose.AgentContextManifest, CancellationToken.None);
        Assert.NotNull(sealedFile);

        return new SeededManifest(sealedFile!.RelativeStoragePath, sealedFile.ByteLength, sealedFile.ContentHash, text);
    }

    /// <summary>Never leaks either the junction or target path: only the process's own exit code
    /// and stderr text are surfaced to a failing assertion. Mirrors the identical helper in
    /// <c>PackageEntrypointResolverTests</c>, the precedent slice's own reparse-point test fixture.</summary>
    private static JunctionCreationResult TryCreateJunction(string junctionPath, string targetPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(junctionPath);
            startInfo.ArgumentList.Add(targetPath);

            using var process = Process.Start(startInfo)!;
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            var succeeded = process.ExitCode == 0 && Directory.Exists(junctionPath);
            return new JunctionCreationResult(succeeded, process.ExitCode, standardError);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new JunctionCreationResult(false, -1, exception.GetType().Name);
        }
    }

    private readonly record struct JunctionCreationResult(bool Succeeded, int ExitCode, string StandardError);

    private sealed record SeededManifest(string RelativePath, long ByteLength, string ContentHash, string Text);

    /// <summary>A minimal, deterministic hand-rolled fake — this test suite has no mocking
    /// framework dependency, matching every other Infrastructure/Application test double in this
    /// codebase (e.g. <c>FakeArtifactStore</c> in <c>CreateCodexPlanningAttemptCommandHandlerTests</c>).
    /// Never spawns a real process: it only records the request <see cref="CodexPlanningAdapter"/>
    /// built and returns a scripted result.</summary>
    private sealed class FakeProcessExecutionAdapter : IProcessExecutionAdapter
    {
        public ProcessExecutionRequest? CapturedRequest { get; private set; }

        public Func<ProcessExecutionRequest, ProcessExecutionResult>? OnExecute { get; set; }

        public Exception? ThrowOnExecute { get; set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;

            if (ThrowOnExecute is { } exception)
            {
                throw exception;
            }

            var result = OnExecute?.Invoke(request) ?? new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.Zero,
            };
            return Task.FromResult(result);
        }
    }
}
