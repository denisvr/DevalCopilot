using System.Diagnostics;
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
/// Exercises <see cref="ClaudeCriticalReviewAdapter"/>'s own contract construction and control
/// flow — CLI argument shape, stdin-only prompt delivery, manifest containment,
/// launch-target revalidation, environment allowlisting, stdout-envelope parsing (including the
/// <c>result</c>-field extraction this provider needs that Codex's file-based final response does
/// not), and closed failure handling. Uses a hand-rolled fake <see cref="IProcessExecutionAdapter"/>
/// (mirroring <c>CodexPlanningAdapterTests</c>' own <c>FakeProcessExecutionAdapter</c>) rather than
/// a real OS process: this adapter must never actually launch a real Claude Code binary in a test.
/// The real <see cref="FilesystemArtifactStore"/> is used (rooted at a disposable temp directory)
/// because manifest verification and the final-response artifact write are genuine filesystem
/// behavior this adapter depends on.
/// </summary>
public sealed class ClaudeCriticalReviewAdapterTests : IDisposable
{
    private readonly string _artifactRoot;
    private readonly FilesystemArtifactStore _artifactStore;
    private readonly string _workspacePath;

    public ClaudeCriticalReviewAdapterTests()
    {
        _artifactRoot = Path.Combine(Path.GetTempPath(), $"devalcopilot-claude-adapter-artifacts-{Guid.NewGuid():N}");
        _artifactStore = new FilesystemArtifactStore(_artifactRoot);
        _workspacePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-claude-adapter-workspace-{Guid.NewGuid():N}");
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
        var executablePath = CreateLaunchFile("fake-claude-direct.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Exited, result.Outcome);
        Assert.NotNull(fake.CapturedRequest);
        var arguments = fake.CapturedRequest!.Arguments.ToArray();

        var expectedSchemaJson = JsonSerializer.Serialize(ClaudeCriticalReviewOutputSchema.BuildSchemaDocument());
        Assert.Equal(25, arguments.Length);
        Assert.Equal("--print", arguments[0]);
        Assert.Equal("--input-format", arguments[1]);
        Assert.Equal("text", arguments[2]);
        Assert.Equal("--output-format", arguments[3]);
        Assert.Equal("json", arguments[4]);
        Assert.Equal("--json-schema", arguments[5]);
        Assert.Equal(expectedSchemaJson, arguments[6]);
        Assert.Equal("--safe-mode", arguments[7]);
        Assert.Equal("--restricted", arguments[8]);
        Assert.Equal("--disable-slash-commands", arguments[9]);
        Assert.Equal("--no-chrome", arguments[10]);
        Assert.Equal("--permission-prompts", arguments[11]);
        Assert.Equal("none", arguments[12]);
        Assert.Equal("--prompt-suggestions", arguments[13]);
        Assert.Equal("false", arguments[14]);
        Assert.Equal("--tools", arguments[15]);
        Assert.Equal(string.Empty, arguments[16]);
        Assert.Equal("--strict-mcp-config", arguments[17]);
        Assert.Equal("--permission-mode", arguments[18]);
        Assert.Equal("plan", arguments[19]);
        Assert.Equal("--no-session-persistence", arguments[20]);
        Assert.Equal("--session-id", arguments[21]);
        Assert.True(Guid.TryParse(arguments[22], out _));
        Assert.Equal("--max-turns", arguments[23]);
        Assert.Equal("1", arguments[24]);

        Assert.Equal(executablePath, fake.CapturedRequest.ExecutablePath);
        Assert.Equal(_workspacePath, fake.CapturedRequest.WorkingDirectory);
        Assert.Equal(_workspacePath, fake.CapturedRequest.ApprovedRoot);
    }

    /// <summary>
    /// The exact defense Task 1's isolation hardening exists for: proves every dangerous,
    /// customization-enabling, or ambiguous argument the installed CLI supports is genuinely
    /// absent, not merely that the expected arguments are present. A future accidental addition of
    /// any of these — e.g. re-introducing <c>--bare</c>, a resume flag, or an arbitrary settings
    /// override — fails this test immediately.
    /// </summary>
    [Fact]
    public async Task No_settings_plugin_mcp_resume_chrome_dangerous_permission_model_or_repository_controlled_argument_is_ever_passed()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-isolation.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.NotNull(fake.CapturedRequest);
        var arguments = fake.CapturedRequest!.Arguments;

        string[] forbiddenArguments =
        [
            "--bare",
            "--continue", "-c",
            "--resume", "-r",
            "--fork-session",
            "--dangerously-skip-permissions",
            "--allow-dangerously-skip-permissions",
            "--mcp-config",
            "--add-dir",
            "--model",
            "--settings",
            "--plugin-dir",
            "--plugin-url",
            "--agent", "--agents",
            "--system-prompt", "--system-prompt-file", "--append-system-prompt", "--append-system-prompt-file",
            "--chrome",
            "--ide",
            "--cloud",
            "--remote-control",
            "--teleport",
            "--worktree", "-w",
            "--from-pr",
            "--file",
            "--betas",
            "--setting-sources",
            "--effort",
        ];
        foreach (var forbidden in forbiddenArguments)
        {
            Assert.DoesNotContain(forbidden, arguments);
        }

        // A dangerous or ambiguous *value* for an argument this adapter does pass would be just
        // as unsafe as an outright forbidden argument — the permission mode and permission-prompt
        // target must be exactly the locked-down values, never anything more permissive.
        Assert.DoesNotContain("bypassPermissions", arguments);
        Assert.DoesNotContain("acceptEdits", arguments);
        Assert.DoesNotContain("auto", arguments);
        Assert.DoesNotContain("dontAsk", arguments);
        Assert.DoesNotContain("host", arguments);

        var permissionModeIndex = Array.IndexOf(arguments.ToArray(), "--permission-mode");
        Assert.True(permissionModeIndex >= 0);
        Assert.Equal("plan", arguments[permissionModeIndex + 1]);

        var permissionPromptsIndex = Array.IndexOf(arguments.ToArray(), "--permission-prompts");
        Assert.True(permissionPromptsIndex >= 0);
        Assert.Equal("none", arguments[permissionPromptsIndex + 1]);
    }

    [Fact]
    public async Task Two_invocations_receive_two_distinct_fresh_session_ids()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-session-fresh.exe");

        var firstFake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeCriticalReviewAdapter(firstFake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        await adapter.InvokeAsync(request, CancellationToken.None);
        var firstSessionIdArgument = firstFake.CapturedRequest!.Arguments[22];

        var secondFake = new FakeProcessExecutionAdapter();
        var secondAdapter = new ClaudeCriticalReviewAdapter(secondFake, _artifactStore);
        await secondAdapter.InvokeAsync(request, CancellationToken.None);
        var secondSessionIdArgument = secondFake.CapturedRequest!.Arguments[22];

        Assert.NotEqual(firstSessionIdArgument, secondSessionIdArgument);
    }

    [Fact]
    public async Task The_context_manifest_is_supplied_only_via_stdin_and_never_appears_as_a_command_line_argument()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        const string ManifestMarker = "MANIFEST-CONTENT-MUST-ONLY-ARRIVE-VIA-STDIN-9f31ab";
        var manifest = await SeedSealedManifestAsync(runId, attemptId, $$"""{"marker":"{{ManifestMarker}}"}""");
        var executablePath = CreateLaunchFile("fake-claude-stdin.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

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
        var executablePath = CreateLaunchFile("fake-claude-escape.exe");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, "../../outside-the-artifact-root.txt", 10, "sha256:" + new string('0', 64),
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.CapturedRequest);
    }

    [Fact]
    public async Task An_absolute_context_manifest_path_is_rejected_rather_than_trusted()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var executablePath = CreateLaunchFile("fake-claude-absolute-manifest.exe");
        var outsideFile = Path.Combine(_workspacePath, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "not a real sealed artifact");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, outsideFile, 10, "sha256:" + new string('0', 64),
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.CapturedRequest);
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
        File.WriteAllText(Path.Combine(realTargetDirectory, "fake-claude.exe"), string.Empty);

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
            var executablePath = Path.Combine(junctionDirectory, "fake-claude.exe");
            var fake = new FakeProcessExecutionAdapter();
            var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
            var request = new CriticalReviewInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
                executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

            var result = await adapter.InvokeAsync(request, CancellationToken.None);

            Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
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
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            missingExecutablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.CapturedRequest);
    }

    [Fact]
    public async Task A_launch_executable_path_that_is_not_fully_qualified_fails_closed()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");

        var fake = new FakeProcessExecutionAdapter();
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            "claude.exe", TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Null(fake.CapturedRequest);
    }

    [Fact]
    public async Task Only_the_minimal_non_secret_environment_allowlist_reaches_the_child_never_path()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-env.exe");

        var previousClaudeConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", @"C:\fake-claude-config-dir-for-tests");
        try
        {
            var fake = new FakeProcessExecutionAdapter();
            var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
            var request = new CriticalReviewInvocationRequest(
                runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
                executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

            var result = await adapter.InvokeAsync(request, CancellationToken.None);

            Assert.Equal(CriticalReviewInvocationOutcome.Exited, result.Outcome);
            Assert.NotNull(fake.CapturedRequest);
            var environment = fake.CapturedRequest!.EnvironmentVariables;
            Assert.False(environment.ContainsKey("PATH"));
            Assert.Equal(@"C:\fake-claude-config-dir-for-tests", environment["CLAUDE_CONFIG_DIR"]);

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
            AssertResultNeverContains(result, @"C:\fake-claude-config-dir-for-tests");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousClaudeConfigDir);
        }
    }

    [Fact]
    public async Task A_non_zero_exit_from_the_provider_produces_a_safe_closed_failed_outcome_preserving_truncation_flags()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-authfail.exe");

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
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
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
        var executablePath = CreateLaunchFile("fake-claude-throws.exe");

        const string SensitiveDetail = "sensitive-failure-detail-must-not-leak-83bd2f";
        var fake = new FakeProcessExecutionAdapter { ThrowOnExecute = new InvalidOperationException(SensitiveDetail) };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        AssertResultNeverContains(result, SensitiveDetail);
    }

    [Fact]
    public async Task A_cancellation_during_process_execution_propagates_rather_than_being_swallowed_as_a_failed_outcome()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-cancel.exe");

        var fake = new FakeProcessExecutionAdapter { ThrowOnExecute = new OperationCanceledException() };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.InvokeAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task A_result_field_carried_as_a_json_string_is_extracted_and_sealed_as_the_final_response_artifact()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-string-result.exe");

        const string FinalResponseJson = """{"decision":"accept","summary":"looks fine","rationale":"nothing material"}""";
        var envelope = JsonSerializer.Serialize(new
        {
            is_error = false,
            result = FinalResponseJson,
            session_id = "session-string-result",
        });

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = envelope,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Exited, result.Outcome);
        Assert.Equal("session-string-result", result.ProviderSessionId);
        var resultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        Assert.True(File.Exists(resultPath));
        var writtenText = await File.ReadAllTextAsync(resultPath);
        Assert.Equal(FinalResponseJson, writtenText);
    }

    [Fact]
    public async Task A_result_field_carried_as_a_raw_json_object_is_re_serialized_faithfully_not_garbled()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-object-result.exe");

        const string EnvelopeJson = """
            {"is_error":false,"result":{"decision":"accept","summary":"looks fine","rationale":"nothing material"},"session_id":"session-object-result"}
            """;

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = EnvelopeJson,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Exited, result.Outcome);
        Assert.Equal("session-object-result", result.ProviderSessionId);
        var resultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        Assert.True(File.Exists(resultPath));
        var writtenText = await File.ReadAllTextAsync(resultPath);

        // Re-parse rather than compare raw text byte-for-byte: the adapter re-serializes via
        // JsonElement.GetRawText, which is not guaranteed to reproduce identical whitespace, but
        // must reproduce every field and value faithfully.
        using var writtenDocument = JsonDocument.Parse(writtenText);
        Assert.Equal("accept", writtenDocument.RootElement.GetProperty("decision").GetString());
        Assert.Equal("looks fine", writtenDocument.RootElement.GetProperty("summary").GetString());
        Assert.Equal("nothing material", writtenDocument.RootElement.GetProperty("rationale").GetString());
    }

    [Fact]
    public async Task An_is_error_true_envelope_produces_a_failed_outcome_even_when_the_process_exit_code_is_zero()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-is-error.exe");

        var envelope = JsonSerializer.Serialize(new
        {
            is_error = true,
            result = "max turns reached",
            session_id = "session-is-error",
        });

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = envelope,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Null(result.ProviderSessionId);
        var resultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        Assert.False(File.Exists(resultPath));
    }

    [Fact]
    public async Task Stdout_that_is_not_valid_json_fails_closed_without_throwing()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-malformed-not-json.exe");

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = "this is not json at all { [ garbled",
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Null(result.ProviderSessionId);
    }

    [Fact]
    public async Task Stdout_that_is_valid_json_but_not_an_object_fails_closed_without_throwing()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-malformed-not-object.exe");

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = """["not", "an", "object"]""",
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Null(result.ProviderSessionId);
    }

    [Fact]
    public async Task An_envelope_missing_the_result_field_fails_closed_without_throwing()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-missing-result.exe");

        var envelope = JsonSerializer.Serialize(new { is_error = false, session_id = "session-missing-result" });

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = envelope,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        var resultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        Assert.False(File.Exists(resultPath));
    }

    [Fact]
    public async Task An_envelope_missing_the_is_error_field_entirely_fails_closed_rather_than_defaulting_to_success()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-missing-is-error.exe");

        var envelope = JsonSerializer.Serialize(new { result = "{}", session_id = "session-missing-is-error" });

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = envelope,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Null(result.ProviderSessionId);
        var resultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        Assert.False(File.Exists(resultPath));
    }

    [Fact]
    public async Task An_envelope_whose_is_error_field_is_not_a_genuine_boolean_fails_closed()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-wrong-is-error-type.exe");

        // A provider that reports is_error as the string "false" rather than the JSON literal
        // false is exactly the kind of ambiguous shape this adapter never guesses about.
        var envelope = """{"is_error":"false","result":"{}","session_id":"session-wrong-is-error-type"}""";

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = envelope,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Null(result.ProviderSessionId);
        var resultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        Assert.False(File.Exists(resultPath));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public async Task An_envelope_with_a_malformed_session_id_of_the_wrong_type_or_blank_fails_the_whole_envelope_closed(string sessionIdJsonLiteral)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile($"fake-claude-malformed-session-id-{Guid.NewGuid():N}.exe");

        var envelope = $$"""{"is_error":false,"result":"{}","session_id":{{sessionIdJsonLiteral}}}""";

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = envelope,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        // A present-but-malformed session id rejects the whole envelope — never just the
        // session id field on its own — since a provider that cannot even shape its own
        // bookkeeping field correctly is not a source whose result should be trusted either.
        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Null(result.ProviderSessionId);
        var resultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        Assert.False(File.Exists(resultPath));
    }

    [Fact]
    public async Task An_envelope_with_an_overlong_session_id_fails_the_whole_envelope_closed()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-overlong-session-id.exe");

        var overlongSessionId = new string('a', 257);
        var envelope = JsonSerializer.Serialize(new { is_error = false, result = "{}", session_id = overlongSessionId });

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = envelope,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Failed, result.Outcome);
        Assert.Null(result.ProviderSessionId);
    }

    [Fact]
    public async Task A_null_session_id_is_tolerated_exactly_like_an_absent_field()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-null-session-id.exe");

        var envelope = JsonSerializer.Serialize(new { is_error = false, result = "{}", session_id = (string?)null });

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = envelope,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Exited, result.Outcome);
        Assert.Null(result.ProviderSessionId);
        var resultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        Assert.True(File.Exists(resultPath));
    }

    [Fact]
    public async Task Unknown_harmless_envelope_metadata_is_tolerated_and_ignored()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var manifest = await SeedSealedManifestAsync(runId, attemptId, "manifest content");
        var executablePath = CreateLaunchFile("fake-claude-extra-metadata.exe");

        // Real fields this CLI's own envelope carries that this adapter never inspects — their
        // presence must never cause a rejection; this is a fixed contract against the fields
        // this adapter actually needs, not a strict schema over every field the provider emits.
        var envelope = JsonSerializer.Serialize(new
        {
            type = "result",
            subtype = "success",
            is_error = false,
            num_turns = 1,
            total_cost_usd = 0.0041,
            duration_ms = 1523,
            result = """{"decision":"accept","summary":"looks fine","rationale":"nothing material"}""",
            session_id = "session-extra-metadata",
        });

        var fake = new FakeProcessExecutionAdapter
        {
            OnExecute = _ => new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = envelope,
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromSeconds(1),
            },
        };
        var adapter = new ClaudeCriticalReviewAdapter(fake, _artifactStore);
        var request = new CriticalReviewInvocationRequest(
            runId, attemptId, _workspacePath, manifest.RelativePath, manifest.ByteLength, manifest.ContentHash,
            executablePath, TimeSpan.FromSeconds(30), 65536, 131072);

        var result = await adapter.InvokeAsync(request, CancellationToken.None);

        Assert.Equal(CriticalReviewInvocationOutcome.Exited, result.Outcome);
        Assert.Equal("session-extra-metadata", result.ProviderSessionId);
        var resultPath = _artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentFinalResponse);
        Assert.True(File.Exists(resultPath));
    }

    private static void AssertResultNeverContains(CriticalReviewInvocationResult result, string forbidden)
    {
        foreach (var property in typeof(CriticalReviewInvocationResult).GetProperties())
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
    /// <c>CodexPlanningAdapterTests</c>, which itself mirrors <c>PackageEntrypointResolverTests</c>.</summary>
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
    /// codebase (e.g. <c>CodexPlanningAdapterTests.FakeProcessExecutionAdapter</c>). Never spawns a
    /// real process: it only records the request <see cref="ClaudeCriticalReviewAdapter"/> built
    /// and returns a scripted result.</summary>
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
                StandardOutput = JsonSerializer.Serialize(new { is_error = false, result = "{}", session_id = (string?)null }),
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.Zero,
            };
            return Task.FromResult(result);
        }
    }
}
