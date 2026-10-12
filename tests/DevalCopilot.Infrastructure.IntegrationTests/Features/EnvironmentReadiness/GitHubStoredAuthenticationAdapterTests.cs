using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetGitHubStoredAuthentication;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.Processes;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// The stored GitHub CLI authentication observer (ADR-0038). Request shape, environment, single-flight and every closed
/// state are proved against a recording process double; the real process boundary (argv, environment, caps, timeout,
/// cancellation and cleanup) is proved against the isolated <c>ProcessExecutionFixture</c> "auth" double. A fixture that
/// prints already-filtered JSON proves the process boundary only: it never proves how the installed GitHub CLI formats
/// <c>--json hosts</c> or evaluates <c>--jq</c>, and no real <c>gh</c>, account, credential or network is ever used.
/// </summary>
public sealed class GitHubStoredAuthenticationAdapterTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");
    private const string GhPath = @"C:\safe\gh.exe";

    // Restated independently of the production constant: the exact privacy projection the child must be asked for.
    private const string ExpectedSelector =
        "if (.hosts | type) == \"object\" and (.hosts | length) == 0 then {entries: []} "
        + "elif (.hosts | type) == \"object\" and (.hosts[\"github.com\"] | type) == \"array\" then "
        + "{entries: [.hosts[\"github.com\"][] | {active: (.active == true), "
        + "state: (if .state == \"success\" or .state == \"error\" then .state else \"other\" end)}]} "
        + "else {invalid: true} end";

    private readonly string _scratch;

    public GitHubStoredAuthenticationAdapterTests()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"devalcopilot-gh-auth-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }

    private sealed class RecordingProcessAdapter(Func<ProcessExecutionRequest, CancellationToken, Task<ProcessExecutionResult>> run)
        : IProcessExecutionAdapter
    {
        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return run(request, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static ProcessExecutionResult Result(
        string stdout, int exitCode = 0, bool stdoutTruncated = false, bool stderrTruncated = false, string stderr = "",
        ProcessExecutionOutcome outcome = ProcessExecutionOutcome.Exited) => new()
        {
            Outcome = outcome,
            ExitCode = outcome == ProcessExecutionOutcome.Exited ? exitCode : null,
            StandardOutput = stdout,
            StandardOutputTruncated = stdoutTruncated,
            StandardError = stderr,
            StandardErrorTruncated = stderrTruncated,
            Duration = TimeSpan.FromMilliseconds(5),
        };

    private GitHubStoredAuthenticationAdapter Create(
        IProcessExecutionAdapter process, Func<string, string?>? environment = null, TimeSpan? timeout = null) =>
        new(process, new FixedTimeProvider(Now), () => _scratch, environment ?? (_ => null), timeout);

    private Task<GitHubStoredAuthenticationObservation> ObserveStdoutAsync(string stdout, int exitCode = 0)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(stdout, exitCode)));
        return Create(process).ObserveAsync(GhPath, CancellationToken.None);
    }

    [Fact]
    public async Task The_request_is_one_literal_argv_inside_the_host_scratch_with_the_fixed_caps_and_no_sinks_or_input()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result("""{"entries":[]}""")));

        await Create(process).ObserveAsync(GhPath, CancellationToken.None);

        var request = Assert.Single(process.Requests);
        Assert.Equal(GhPath, request.ExecutablePath);
        Assert.Equal(
            ["auth", "status", "--hostname", "github.com", "--active", "--json", "hosts", "--jq", ExpectedSelector],
            request.Arguments);
        Assert.Equal(_scratch, request.WorkingDirectory);
        Assert.Equal(_scratch, request.ApprovedRoot);
        Assert.Equal(TimeSpan.FromSeconds(15), request.Timeout);
        Assert.Equal(8 * 1024, request.MaxBytesPerStream);
        Assert.Equal(16 * 1024, request.MaxTotalCapturedBytes);
        Assert.Null(request.StandardOutputSinkPath);
        Assert.Null(request.StandardErrorSinkPath);
        Assert.Null(request.StandardInput);
    }

    [Fact]
    public async Task The_privacy_selector_constant_is_the_pinned_expression_and_names_no_account_field()
    {
        Assert.Equal(ExpectedSelector, GitHubStoredAuthenticationAdapter.PrivacySelector);
        foreach (var forbidden in new[] { "login", "token", "scopes", "error", "gitProtocol", "host\"" })
        {
            Assert.DoesNotContain(forbidden, ExpectedSelector.Replace("\"error\"", string.Empty), StringComparison.Ordinal);
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_child_receives_only_the_allowlisted_profile_variables_and_fixed_controls_never_ambient_credentials_or_overrides()
    {
        var ambient = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = @"C:\Windows",
            ["USERPROFILE"] = @"C:\Users\someone",
            ["APPDATA"] = @"C:\Users\someone\AppData\Roaming",
            ["LOCALAPPDATA"] = @"C:\Users\someone\AppData\Local",
            ["TEMP"] = @"C:\Temp",
            ["GH_TOKEN"] = "sentinel-token",
            ["GITHUB_TOKEN"] = "sentinel-token",
            ["GH_ENTERPRISE_TOKEN"] = "sentinel-token",
            ["GITHUB_ENTERPRISE_TOKEN"] = "sentinel-token",
            ["GH_HOST"] = "evil.example",
            ["GH_CONFIG_DIR"] = @"C:\evil",
            ["XDG_CONFIG_HOME"] = @"C:\evil",
            ["GH_DEBUG"] = "api",
            ["HTTPS_PROXY"] = "http://evil.example",
            ["BROWSER"] = "evil",
            ["PAGER"] = "evil",
            ["GH_PAGER"] = "evil",
            ["GH_PROMPT_DISABLED"] = "0",
            ["GH_TELEMETRY"] = "true",
            ["PATH"] = @"C:\evil",
        };
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result("""{"entries":[]}""")));

        await Create(process, name => ambient.GetValueOrDefault(name)).ObserveAsync(GhPath, CancellationToken.None);

        var environment = Assert.Single(process.Requests).EnvironmentVariables;
        Assert.Equal(
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["APPDATA"] = @"C:\Users\someone\AppData\Roaming",
                ["DO_NOT_TRACK"] = "1",
                ["GH_NO_EXTENSION_UPDATE_NOTIFIER"] = "1",
                ["GH_NO_UPDATE_NOTIFIER"] = "1",
                ["GH_PROMPT_DISABLED"] = "1",
                ["GH_SPINNER_DISABLED"] = "1",
                ["GH_TELEMETRY"] = "false",
                ["LOCALAPPDATA"] = @"C:\Users\someone\AppData\Local",
                ["NO_COLOR"] = "1",
                ["SystemRoot"] = @"C:\Windows",
                ["TEMP"] = @"C:\Temp",
                ["USERPROFILE"] = @"C:\Users\someone",
            },
            new SortedDictionary<string, string>(environment.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal));
        Assert.DoesNotContain("sentinel-token", string.Join('|', environment.Values));
    }

    [Fact]
    public async Task Absent_or_empty_allowlisted_variables_are_not_invented()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result("""{"entries":[]}""")));

        await Create(process, name => name == "TEMP" ? string.Empty : null).ObserveAsync(GhPath, CancellationToken.None);

        var environment = Assert.Single(process.Requests).EnvironmentVariables;
        Assert.DoesNotContain("TEMP", environment.Keys);
        Assert.DoesNotContain("USERPROFILE", environment.Keys);
        Assert.Equal("1", environment["GH_PROMPT_DISABLED"]);
    }

    [Theory]
    [InlineData("""{"entries":[]}""", GitHubStoredAuthenticationStatus.NoActiveAccount)]
    [InlineData("""{"entries":[{"active":true,"state":"success"}]}""", GitHubStoredAuthenticationStatus.Authenticated)]
    [InlineData("""{"entries":[{"active":true,"state":"error"}]}""", GitHubStoredAuthenticationStatus.IssueReported)]
    [InlineData("  {\n \"entries\": [ { \"state\": \"success\", \"active\": true } ] }\r\n", GitHubStoredAuthenticationStatus.Authenticated)]
    public async Task Coherent_filtered_answers_map_to_the_three_observed_statuses_with_the_host_clock(
        string stdout, GitHubStoredAuthenticationStatus expected)
    {
        var observation = await ObserveStdoutAsync(stdout);

        Assert.Equal(expected, observation.Status);
        Assert.Equal(Now, observation.ObservedAtUtc);
    }

    [Theory]
    [InlineData("""{"entries":[{"active":true,"state":"timeout"}]}""")]
    [InlineData("""{"entries":[{"active":true,"state":"other"}]}""")]
    [InlineData("""{"entries":[{"active":true,"state":"Success"}]}""")]
    [InlineData("""{"entries":[{"active":false,"state":"success"}]}""")]
    [InlineData("""{"entries":[{"active":true,"state":"success"},{"active":true,"state":"success"}]}""")]
    [InlineData("""{"entries":[{"active":true,"state":"success"},{"active":true,"state":"error"}]}""")]
    [InlineData("""{"entries":[{"active":true}]}""")]
    [InlineData("""{"entries":[{"state":"success"}]}""")]
    [InlineData("""{"entries":[{"active":"true","state":"success"}]}""")]
    [InlineData("""{"entries":[{"active":true,"state":1}]}""")]
    [InlineData("""{"entries":[{"active":true,"state":null}]}""")]
    [InlineData("""{"entries":[{"active":true,"state":"success","login":"someone"}]}""")]
    [InlineData("""{"entries":[{"active":true,"state":"success","token":"sentinel-token"}]}""")]
    [InlineData("""{"entries":[{"active":true,"active":true,"state":"success"}]}""")]
    [InlineData("""{"entries":[{"active":true,"state":"success","state":"success"}]}""")]
    [InlineData("""{"entries":[],"entries":[]}""")]
    [InlineData("""{"entries":[],"login":"someone"}""")]
    [InlineData("""{"entries":{}}""")]
    [InlineData("""{"entries":null}""")]
    [InlineData("""{"entries":["success"]}""")]
    [InlineData("""{"entries":[null]}""")]
    [InlineData("""{"invalid":true}""")]
    [InlineData("""{"entries":[],"invalid":true}""")]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    [InlineData("\"success\"")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("""{"entries":[]""")]
    [InlineData("""{"entries":[]} {"entries":[]}""")]
    [InlineData("""{"entries":[]} trailing""")]
    [InlineData("""{"entries":[],}""")]
    [InlineData("""{"entries":[]} // comment""")]
    [InlineData("github.com\n  ✓ Logged in to github.com account someone (keyring)")]
    public async Task Every_other_shape_is_unknown_without_a_timestamp(string stdout)
    {
        var observation = await ObserveStdoutAsync(stdout);

        Assert.Equal(GitHubStoredAuthenticationObservation.Unknown, observation);
    }

    [Fact]
    public async Task A_zero_exit_alone_is_never_authentication()
    {
        Assert.Equal(GitHubStoredAuthenticationStatus.Unknown, (await ObserveStdoutAsync(string.Empty)).Status);
        Assert.Equal(GitHubStoredAuthenticationStatus.Unknown, (await ObserveStdoutAsync("ok")).Status);
    }

    [Fact]
    public async Task A_zero_exit_that_reports_an_authentication_error_is_issue_reported_not_authenticated()
    {
        var observation = await ObserveStdoutAsync("""{"entries":[{"active":true,"state":"error"}]}""");

        Assert.Equal(GitHubStoredAuthenticationStatus.IssueReported, observation.Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task A_nonzero_exit_is_unknown_even_with_a_valid_looking_body(int exitCode)
    {
        var observation = await ObserveStdoutAsync("""{"entries":[{"active":true,"state":"success"}]}""", exitCode);

        Assert.Equal(GitHubStoredAuthenticationObservation.Unknown, observation);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_truncated_stream_is_unknown_even_with_a_valid_looking_body(bool stdoutTruncated, bool stderrTruncated)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(
            Result("""{"entries":[{"active":true,"state":"success"}]}""", stdoutTruncated: stdoutTruncated, stderrTruncated: stderrTruncated)));

        var observation = await Create(process).ObserveAsync(GhPath, CancellationToken.None);

        Assert.Equal(GitHubStoredAuthenticationObservation.Unknown, observation);
    }

    [Fact]
    public async Task Stderr_content_never_changes_the_outcome()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(
            Result("""{"entries":[{"active":true,"state":"success"}]}""", stderr: "X Failed to log in ghp_sentinel")));

        var observation = await Create(process).ObserveAsync(GhPath, CancellationToken.None);

        Assert.Equal(GitHubStoredAuthenticationStatus.Authenticated, observation.Status);
        Assert.DoesNotContain("sentinel", observation.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timed_out_process_is_unknown()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(
            Result("""{"entries":[{"active":true,"state":"success"}]}""", outcome: ProcessExecutionOutcome.TimedOut)));

        Assert.Equal(GitHubStoredAuthenticationObservation.Unknown, await Create(process).ObserveAsync(GhPath, CancellationToken.None));
    }

    [Fact]
    public async Task An_os_process_start_failure_is_unknown_and_exposes_no_exception_detail()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromException<ProcessExecutionResult>(
            new System.ComponentModel.Win32Exception(5, @"cannot start C:\secret\path ghp_sentinel")));

        var observation = await Create(process).ObserveAsync(GhPath, CancellationToken.None);

        Assert.Equal(GitHubStoredAuthenticationObservation.Unknown, observation);
        Assert.DoesNotContain("sentinel", observation.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_vetted_executable_that_vanished_is_unknown()
    {
        var process = new RecordingProcessAdapter((request, _) => Task.FromException<ProcessExecutionResult>(
            new ArgumentException(@"does not exist C:\secret\path", nameof(ProcessExecutionRequest.ExecutablePath))));

        Assert.Equal(GitHubStoredAuthenticationObservation.Unknown, await Create(process).ObserveAsync(GhPath, CancellationToken.None));
    }

    public static TheoryData<Exception> ProgrammingFaults => new()
    {
        new InvalidOperationException("boom"),
        new ArgumentException("bad request", nameof(ProcessExecutionRequest.WorkingDirectory)),
        new ArgumentOutOfRangeException(nameof(ProcessExecutionRequest.Timeout)),
        new NullReferenceException(),
        new IOException("disk"),
        new NotSupportedException(),
    };

    [Theory]
    [MemberData(nameof(ProgrammingFaults))]
    public async Task An_unexpected_process_port_fault_propagates_and_never_becomes_a_status(Exception fault)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromException<ProcessExecutionResult>(fault));

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => Create(process).ObserveAsync(GhPath, CancellationToken.None));

        Assert.Same(fault, thrown);
    }

    [Theory]
    [MemberData(nameof(ProgrammingFaults))]
    public async Task The_slot_is_released_through_finally_when_a_fault_propagates(Exception fault)
    {
        var calls = 0;
        var process = new RecordingProcessAdapter((_, _) =>
            ++calls == 1 ? Task.FromException<ProcessExecutionResult>(fault) : Task.FromResult(Result("""{"entries":[]}""")));
        var adapter = Create(process);

        await Assert.ThrowsAnyAsync<Exception>(() => adapter.ObserveAsync(GhPath, CancellationToken.None));

        Assert.Equal(GitHubStoredAuthenticationStatus.NoActiveAccount, (await adapter.ObserveAsync(GhPath, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_scratch_directory_fault_propagates_and_releases_the_slot()
    {
        var calls = 0;
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result("""{"entries":[]}""")));
        var adapter = new GitHubStoredAuthenticationAdapter(
            process, new FixedTimeProvider(Now), () => ++calls == 1 ? throw new IOException("scratch") : _scratch, _ => null, null);

        await Assert.ThrowsAsync<IOException>(() => adapter.ObserveAsync(GhPath, CancellationToken.None));

        Assert.Equal(GitHubStoredAuthenticationStatus.NoActiveAccount, (await adapter.ObserveAsync(GhPath, CancellationToken.None)).Status);
    }


    [Fact]
    public async Task Cancellation_is_raised_not_reported_as_a_status_and_releases_the_probe()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var process = new RecordingProcessAdapter((_, token) =>
        {
            calls++;
            return calls == 1
                ? Task.FromResult(Result(string.Empty, outcome: ProcessExecutionOutcome.Cancelled))
                : Task.FromResult(Result("""{"entries":[]}"""));
        });
        var adapter = Create(process);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.ObserveAsync(GhPath, cancellation.Token));
        var next = await adapter.ObserveAsync(GhPath, CancellationToken.None);

        Assert.Equal(GitHubStoredAuthenticationStatus.NoActiveAccount, next.Status);
    }

    [Fact]
    public async Task A_thrown_cancellation_propagates_and_releases_the_probe()
    {
        var calls = 0;
        var process = new RecordingProcessAdapter((_, _) =>
            ++calls == 1 ? throw new OperationCanceledException() : Task.FromResult(Result("""{"entries":[]}""")));
        var adapter = Create(process);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.ObserveAsync(GhPath, CancellationToken.None));

        Assert.Equal(GitHubStoredAuthenticationStatus.NoActiveAccount, (await adapter.ObserveAsync(GhPath, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_concurrent_request_is_unknown_immediately_with_no_queue_retry_or_extra_spawn_and_the_slot_is_released_after_the_first()
    {
        var release = new TaskCompletionSource<ProcessExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var process = new RecordingProcessAdapter((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.SetResult();
                return release.Task;
            }

            return Task.FromResult(Result("""{"entries":[{"active":true,"state":"success"}]}"""));
        });
        var adapter = Create(process);

        var first = adapter.ObserveAsync(GhPath, CancellationToken.None);
        await started.Task;
        var concurrent = await adapter.ObserveAsync(GhPath, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(GitHubStoredAuthenticationObservation.Unknown, concurrent);
        Assert.Single(process.Requests);

        release.SetResult(Result("""{"entries":[]}"""));
        Assert.Equal(GitHubStoredAuthenticationStatus.NoActiveAccount, (await first).Status);
        Assert.Single(process.Requests);

        var later = await adapter.ObserveAsync(GhPath, CancellationToken.None);
        Assert.Equal(GitHubStoredAuthenticationStatus.Authenticated, later.Status);
        Assert.Equal(2, process.Requests.Count);
    }


    // Independent restatement of what the fixed selector emits for an UNFILTERED `gh auth status --json hosts` answer
    // (never the evaluator itself): the original shape must stay distinguishable from an empty success, and the emitted
    // JSON must carry no account detail. Real formatting and `--jq` evaluation by an installed CLI remain unverified.
    private static string RestatedSelectorOutput(string unfiltered)
    {
        var root = JsonNode.Parse(unfiltered);
        if (root is JsonObject { } emptyRoot && emptyRoot["hosts"] is JsonObject { Count: 0 })
        {
            return new JsonObject { ["entries"] = new JsonArray() }.ToJsonString();
        }

        if (root is JsonObject { } rootObject
            && rootObject["hosts"] is JsonObject hosts
            && hosts["github.com"] is JsonArray accounts)
        {
            var entries = new JsonArray();
            foreach (var account in accounts)
            {
                var accountObject = account!.AsObject();
                var state = accountObject["state"]?.GetValue<string>();
                entries.Add(new JsonObject
                {
                    ["active"] = accountObject["active"] is JsonValue active && active.TryGetValue<bool>(out var flag) && flag,
                    ["state"] = state is "success" or "error" ? state : "other",
                });
            }

            return new JsonObject { ["entries"] = entries }.ToJsonString();
        }

        return new JsonObject { ["invalid"] = true }.ToJsonString();
    }

    private const string Account =
        """{"state":"success","active":true,"host":"github.com","login":"sentinel-login","tokenSource":"keyring","scopes":"repo, sentinel-scope","gitProtocol":"https","error":"sentinel-error"}""";

    public static TheoryData<string, GitHubStoredAuthenticationStatus> UnfilteredResponses => new()
    {
        { "{\"hosts\":{\"github.com\":[" + Account + "]}}", GitHubStoredAuthenticationStatus.Authenticated },
        { "{\"hosts\":{\"github.com\":[" + Account.Replace("\"success\"", "\"error\"") + "]}}", GitHubStoredAuthenticationStatus.IssueReported },
        { "{\"hosts\":{\"github.com\":[" + Account.Replace("\"success\"", "\"timeout\"") + "]}}", GitHubStoredAuthenticationStatus.Unknown },
        { "{\"hosts\":{\"github.com\":[" + Account.Replace("true", "false") + "]}}", GitHubStoredAuthenticationStatus.Unknown },
        { "{\"hosts\":{\"github.com\":[" + Account + "," + Account + "]}}", GitHubStoredAuthenticationStatus.Unknown },
        { "{\"hosts\":{\"github.com\":[]}}", GitHubStoredAuthenticationStatus.NoActiveAccount },
        { "{\"hosts\":{}}", GitHubStoredAuthenticationStatus.NoActiveAccount },
        { "{\"hosts\":{\"example.com\":[]}}", GitHubStoredAuthenticationStatus.Unknown },
        { "{\"hosts\":{\"example.com\":[" + Account + "]}}", GitHubStoredAuthenticationStatus.Unknown },
        { "{\"hosts\":{\"github.com\":[],\"example.com\":[]}}", GitHubStoredAuthenticationStatus.NoActiveAccount },
        { "{\"hosts\":\"\"}", GitHubStoredAuthenticationStatus.Unknown },
        { "{\"hosts\":0}", GitHubStoredAuthenticationStatus.Unknown },
        { "{\"hosts\":{\"github.com\":{}}}", GitHubStoredAuthenticationStatus.Unknown },
        { "{\"hosts\":{\"github.com\":null}}", GitHubStoredAuthenticationStatus.Unknown },
        { "{\"hosts\":[]}", GitHubStoredAuthenticationStatus.Unknown },
        { "{\"hosts\":null}", GitHubStoredAuthenticationStatus.Unknown },
        { "{}", GitHubStoredAuthenticationStatus.Unknown },
    };

    [Theory]
    [MemberData(nameof(UnfilteredResponses))]
    public async Task The_selector_restated_over_synthetic_unfiltered_responses_yields_only_safe_payloads_and_the_expected_status(
        string unfiltered, GitHubStoredAuthenticationStatus expected)
    {
        var safe = RestatedSelectorOutput(unfiltered);

        foreach (var sentinel in new[] { "sentinel-login", "sentinel-scope", "sentinel-error", "keyring", "repo", "https" })
        {
            Assert.DoesNotContain(sentinel, safe, StringComparison.Ordinal);
        }

        Assert.Equal(expected, (await ObserveStdoutAsync(safe)).Status);
    }

    [Fact]
    public async Task An_unfiltered_response_reaching_the_parent_is_never_accepted()
    {
        // If the child ever ignored the selector, account detail must be refused whole rather than reduced.
        var observation = await ObserveStdoutAsync("{\"hosts\":{\"github.com\":[" + Account + "]}}");

        Assert.Equal(GitHubStoredAuthenticationObservation.Unknown, observation);
    }

    // --- The real process boundary, against the isolated fixture (no gh, no account, no network). ---

    private static string BehaviorJson(object behavior) => JsonSerializer.Serialize(behavior);

    private Task WriteBehaviorAsync(object behavior) => File.WriteAllTextAsync(Path.Combine(_scratch, "gh-auth-behavior.json"), BehaviorJson(behavior));

    private GitHubStoredAuthenticationAdapter CreateReal(TimeSpan? timeout = null, Func<string, string?>? environment = null) =>
        new(new ChildProcessExecutionAdapter(), new FixedTimeProvider(Now), () => _scratch, environment ?? (_ => null), timeout);

    private JsonElement ReadInvocation() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(_scratch, "gh-auth-invocation.json"))).RootElement.Clone();

    [Fact]
    public async Task A_real_child_receives_exactly_the_fixed_argv_and_no_ambient_environment_and_authenticates()
    {
        Assert.True(File.Exists(FixtureExecutablePath));
        await WriteBehaviorAsync(new { Stdout = """{"entries":[{"active":true,"state":"success"}]}""" });
        var ambient = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GH_TOKEN"] = "sentinel-token",
            ["GH_HOST"] = "evil.example",
            ["TEMP"] = Path.GetTempPath(),
        };

        // The fixture's mode is its first argument ("auth"), exactly the first argument the adapter passes.
        var observation = await CreateReal(environment: name => ambient.GetValueOrDefault(name))
            .ObserveAsync(FixtureExecutablePath, CancellationToken.None);

        Assert.Equal(GitHubStoredAuthenticationStatus.Authenticated, observation.Status);
        var invocation = ReadInvocation();
        Assert.Equal(
            ["auth", "status", "--hostname", "github.com", "--active", "--json", "hosts", "--jq", ExpectedSelector],
            invocation.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!).ToArray());
        var names = invocation.GetProperty("environment").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Equals("GH_TOKEN", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Equals("GH_HOST", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name.Equals("GH_PROMPT_DISABLED", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("sentinel-token", invocation.GetProperty("environment").GetRawText());
    }

    [Fact]
    public async Task A_real_zero_exit_error_state_is_issue_reported()
    {
        await WriteBehaviorAsync(new { Stdout = """{"entries":[{"active":true,"state":"error"}]}""", ExitCode = 0 });

        var observation = await CreateReal().ObserveAsync(FixtureExecutablePath, CancellationToken.None);

        Assert.Equal(GitHubStoredAuthenticationStatus.IssueReported, observation.Status);
    }

    [Fact]
    public async Task A_real_nonzero_exit_and_unsupported_flags_are_unknown()
    {
        await WriteBehaviorAsync(new { Stderr = "unknown flag: --active", ExitCode = 1 });

        Assert.Equal(GitHubStoredAuthenticationObservation.Unknown, await CreateReal().ObserveAsync(FixtureExecutablePath, CancellationToken.None));
    }

    [Theory]
    [InlineData(8 * 1024 + 1)]
    [InlineData(64 * 1024)]
    public async Task Real_oversized_output_is_truncated_by_the_cap_and_unknown(int bytes)
    {
        await WriteBehaviorAsync(new { StdoutBytes = bytes });

        Assert.Equal(GitHubStoredAuthenticationObservation.Unknown, await CreateReal().ObserveAsync(FixtureExecutablePath, CancellationToken.None));
    }

    [Fact]
    public async Task A_real_timeout_is_unknown_and_the_child_is_gone()
    {
        var pidFile = Path.Combine(_scratch, "pid.txt");
        await WriteBehaviorAsync(new { SleepMs = 60_000, PidFile = pidFile });

        var observation = await CreateReal(TimeSpan.FromMilliseconds(500)).ObserveAsync(FixtureExecutablePath, CancellationToken.None);

        Assert.Equal(GitHubStoredAuthenticationObservation.Unknown, observation);
        AssertProcessGone(int.Parse(await File.ReadAllTextAsync(pidFile)));
    }

    [Fact]
    public async Task Real_cancellation_raises_kills_the_child_and_releases_the_probe()
    {
        var pidFile = Path.Combine(_scratch, "pid.txt");
        await WriteBehaviorAsync(new { SleepMs = 60_000, PidFile = pidFile });
        var adapter = CreateReal(TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();

        var observing = adapter.ObserveAsync(FixtureExecutablePath, cancellation.Token);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(pidFile) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observing);
        AssertProcessGone(int.Parse(await File.ReadAllTextAsync(pidFile)));

        await WriteBehaviorAsync(new { Stdout = """{"entries":[]}""" });
        Assert.Equal(GitHubStoredAuthenticationStatus.NoActiveAccount, (await adapter.ObserveAsync(FixtureExecutablePath, CancellationToken.None)).Status);
    }

    private static void AssertProcessGone(int processId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            Thread.Sleep(100);
        }

        Assert.Fail("The child process was still running.");
    }
}
