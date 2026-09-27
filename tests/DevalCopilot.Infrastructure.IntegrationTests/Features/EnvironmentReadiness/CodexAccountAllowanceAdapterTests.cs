using System.Diagnostics;
using System.Text.Json;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Exercises <see cref="CodexAccountAllowanceAdapter"/> against a real child process — the
/// deterministic <c>ProcessExecutionFixture</c> test double, driven through its Node-script
/// launch shape so a per-test-unique JSON script file (never argv, which this adapter always
/// fixes to exactly <c>app-server --stdio</c>) can script the exact bytes it writes back — because the
/// behavior under test (the live duplex JSON-RPC handshake, response correlation, bounded
/// scanning, and process-tree cleanup) only exists at the real OS process boundary. Mirrors
/// <see cref="Processes.ChildProcessExecutionAdapterTests"/>'s own use of this fixture.
///
/// Every rate-limits fixture below uses the documented response shape: <c>rateLimitsByLimitId</c>
/// maps a bounded limit id (for example <c>"codex"</c>) to a snapshot object carrying its own
/// <c>primary</c>/<c>secondary</c> windows; the legacy <c>rateLimits</c> field is itself one such
/// snapshot, with no id of its own. <c>resetsAt</c> is Unix seconds (an integer), never an ISO
/// string, and both it and <c>windowDurationMins</c> are optional.
/// </summary>
public sealed class CodexAccountAllowanceAdapterTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");

    private const string InitializeOkResponse = """{"id":0,"result":{}}""";

    private readonly string _scratchDirectory;

    public CodexAccountAllowanceAdapterTests()
    {
        _scratchDirectory = Path.Combine(Path.GetTempPath(), $"devalcopilot-account-allowance-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratchDirectory);

        Assert.True(File.Exists(FixtureExecutablePath), $"Expected the fixture executable at '{FixtureExecutablePath}'.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchDirectory))
        {
            Directory.Delete(_scratchDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task An_official_shape_multi_bucket_response_produces_visible_positive_data()
    {
        // Matches the documented example shape exactly: one limit id ("codex") mapping to a
        // snapshot with its own primary/secondary windows, resetsAt as Unix seconds.
        var rateLimitsResult = """
            {"id":1,"result":{"rateLimitsByLimitId":{
                "codex":{
                    "primary":{"usedPercent":42,"windowDurationMins":300,"resetsAt":1790875200},
                    "secondary":{"usedPercent":10,"windowDurationMins":10080,"resetsAt":1791417600}
                }
            },"rateLimits":{"primary":{"usedPercent":1}}}}
            """.ReplaceLineEndings("");

        var adapter = CreateAdapter();
        var requestLogPath = Path.Combine(_scratchDirectory, "official-shape-requests.jsonl");
        var observation = await InvokeWithScriptAsync(adapter, new AppServerScript(
        [
            Step(1, InitializeOkResponse),
            Step(2, rateLimitsResult),
        ], RequestLogPath: requestLogPath));

        Assert.True(observation.IsObserved);
        Assert.NotNull(observation.RetrievedAtUtc);
        var bucket = Assert.Single(observation.Buckets);
        Assert.Equal("codex", bucket.LimitId);
        Assert.Equal(42, bucket.Primary!.UsedPercent);
        Assert.Equal(300, bucket.Primary.WindowDurationMins);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790875200), bucket.Primary.ResetsAtUtc);
        Assert.Equal(10, bucket.Secondary!.UsedPercent);
        Assert.Equal(10080, bucket.Secondary.WindowDurationMins);
        Assert.Equal(
        [
            """{"method":"initialize","id":0,"params":{"clientInfo":{"name":"DevalCopilot","title":"DevalCopilot","version":"1.0.0"},"capabilities":{}}}""",
            """{"method":"initialized","params":{}}""",
            """{"method":"account/rateLimits/read","id":1}""",
        ], File.ReadAllLines(requestLogPath));
    }

    [Fact]
    public async Task Multiple_limit_ids_are_each_represented_as_their_own_bucket_without_an_invented_aggregate()
    {
        var rateLimitsResult = """
            {"id":1,"result":{"rateLimitsByLimitId":{
                "codex":{"primary":{"usedPercent":42,"windowDurationMins":300,"resetsAt":1790875200}},
                "gpt-5":{"primary":{"usedPercent":7,"windowDurationMins":300,"resetsAt":1790875200}}
            }}}
            """.ReplaceLineEndings("");

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            Step(2, rateLimitsResult)));

        Assert.True(observation.IsObserved);
        Assert.Equal(2, observation.Buckets.Count);
        Assert.Contains(observation.Buckets, bucket => bucket.LimitId == "codex" && bucket.Primary!.UsedPercent == 42);
        Assert.Contains(observation.Buckets, bucket => bucket.LimitId == "gpt-5" && bucket.Primary!.UsedPercent == 7);
    }

    [Fact]
    public async Task A_limit_id_that_fails_the_bounded_character_check_fails_closed()
    {
        var rateLimitsResult = """
            {"id":1,"result":{"rateLimitsByLimitId":{
                "codex":{"primary":{"usedPercent":42,"windowDurationMins":300,"resetsAt":1790875200}},
                "<script>bad id":{"primary":{"usedPercent":7,"windowDurationMins":300,"resetsAt":1790875200}}
            }}}
            """.ReplaceLineEndings("");

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            Step(2, rateLimitsResult)));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task More_than_the_bounded_number_of_buckets_fails_closed_without_silently_omitting_one()
    {
        var entries = string.Join(',', Enumerable.Range(0, 17)
            .Select(index => $"\"bucket-{index}\":{{\"primary\":{{\"usedPercent\":{index}}}}}"));
        var response = "{\"id\":1,\"result\":{\"rateLimitsByLimitId\":{" + entries + "}}}";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(
            Step(1, InitializeOkResponse), Step(2, response)));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task Duplicate_limit_ids_fail_closed_instead_of_choosing_one_value()
    {
        var response = """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":1}},"codex":{"primary":{"usedPercent":99}}}}}""";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(
            Step(1, InitializeOkResponse), Step(2, response)));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task A_legacy_rateLimits_response_is_observed_as_one_unlabeled_bucket_when_rateLimitsByLimitId_is_absent()
    {
        var rateLimitsResult = """
            {"id":1,"result":{"rateLimits":{
                "primary":{"usedPercent":5,"windowDurationMins":300,"resetsAt":1790875200},
                "secondary":{"usedPercent":1,"windowDurationMins":10080,"resetsAt":1791417600}
            }}}
            """.ReplaceLineEndings("");

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            Step(2, rateLimitsResult)));

        Assert.True(observation.IsObserved);
        var bucket = Assert.Single(observation.Buckets);
        Assert.Null(bucket.LimitId);
        Assert.Equal(5, bucket.Primary!.UsedPercent);
        Assert.Equal(1, bucket.Secondary!.UsedPercent);
    }

    [Fact]
    public async Task RateLimitsByLimitId_is_preferred_over_a_simultaneously_present_legacy_view_without_combining_both()
    {
        var rateLimitsResult = """
            {"id":1,"result":{
                "rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":77,"windowDurationMins":300,"resetsAt":1790875200}}},
                "rateLimits":{"primary":{"usedPercent":1,"windowDurationMins":300,"resetsAt":1790875200}}
            }}
            """.ReplaceLineEndings("");

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            Step(2, rateLimitsResult)));

        Assert.True(observation.IsObserved);
        var bucket = Assert.Single(observation.Buckets);
        Assert.Equal("codex", bucket.LimitId);
        Assert.Equal(77, bucket.Primary!.UsedPercent);
    }

    [Fact]
    public async Task A_window_the_response_does_not_carry_projects_as_null_without_failing_the_whole_bucket()
    {
        var rateLimitsResult =
            """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":42,"windowDurationMins":300,"resetsAt":1790875200}}}}}""";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            Step(2, rateLimitsResult)));

        Assert.True(observation.IsObserved);
        var bucket = Assert.Single(observation.Buckets);
        Assert.NotNull(bucket.Primary);
        Assert.Null(bucket.Secondary);
    }

    [Theory]
    [InlineData("\"not-a-number\"")]
    [InlineData("142")]
    public async Task A_window_with_an_out_of_range_or_wrongly_typed_usedPercent_projects_as_null(string usedPercentJson)
    {
        var rateLimitsResult =
            """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":USED_PERCENT,"windowDurationMins":300,"resetsAt":1790875200},"secondary":{"usedPercent":1,"windowDurationMins":10080,"resetsAt":1791417600}}}}}"""
                .Replace("USED_PERCENT", usedPercentJson);
        await AssertMalformedPrimaryProjectsAsNullAsync(rateLimitsResult);
    }

    [Fact]
    public async Task A_negative_windowDurationMins_projects_only_that_field_as_null_while_usedPercent_is_preserved()
    {
        var rateLimitsResult =
            """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":42,"windowDurationMins":-1,"resetsAt":1790875200}}}}}""";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            Step(2, rateLimitsResult)));

        Assert.True(observation.IsObserved);
        var bucket = Assert.Single(observation.Buckets);
        Assert.Equal(42, bucket.Primary!.UsedPercent);
        Assert.Null(bucket.Primary.WindowDurationMins);
    }

    [Fact]
    public async Task An_unparseable_resetsAt_projects_only_that_field_as_null_while_usedPercent_is_preserved()
    {
        var rateLimitsResult =
            """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":42,"windowDurationMins":300,"resetsAt":"not-a-unix-timestamp"}}}}}""";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            Step(2, rateLimitsResult)));

        Assert.True(observation.IsObserved);
        var bucket = Assert.Single(observation.Buckets);
        Assert.Equal(42, bucket.Primary!.UsedPercent);
        Assert.Equal(300, bucket.Primary.WindowDurationMins);
        Assert.Null(bucket.Primary.ResetsAtUtc);
    }

    [Fact]
    public async Task Missing_optional_windowDurationMins_and_resetsAt_project_as_null_without_rejecting_the_window()
    {
        var rateLimitsResult = """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":42}}}}}""";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            Step(2, rateLimitsResult)));

        Assert.True(observation.IsObserved);
        var bucket = Assert.Single(observation.Buckets);
        Assert.Equal(42, bucket.Primary!.UsedPercent);
        Assert.Null(bucket.Primary.WindowDurationMins);
        Assert.Null(bucket.Primary.ResetsAtUtc);
    }

    [Fact]
    public async Task A_window_that_is_not_a_json_object_projects_as_null()
    {
        await AssertMalformedPrimaryProjectsAsNullAsync(
            """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":"not-an-object","secondary":{"usedPercent":1,"windowDurationMins":10080,"resetsAt":1791417600}}}}}""");
    }

    private async Task AssertMalformedPrimaryProjectsAsNullAsync(string rateLimitsResult)
    {
        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            Step(2, rateLimitsResult)));

        Assert.True(observation.IsObserved);
        var bucket = Assert.Single(observation.Buckets);
        Assert.Null(bucket.Primary);
        Assert.NotNull(bucket.Secondary);
    }

    [Fact]
    public async Task No_usable_bucket_reports_unknown_rather_than_an_observed_snapshot_with_nothing_to_show()
    {
        var rateLimitsResult = """{"id":1,"result":{"rateLimitsByLimitId":{}}}""";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            Step(2, rateLimitsResult)));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task Unsolicited_notifications_interleaved_with_the_real_reply_are_ignored()
    {
        var rateLimitsResult =
            """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":42,"windowDurationMins":300,"resetsAt":1790875200}}}}}""";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            new AppServerStepScript(2,
            [
                """{"method":"session/updated","params":{}}""",
                """{"method":"item/completed","params":{"noise":true}}""",
                rateLimitsResult,
            ])));

        Assert.True(observation.IsObserved);
        Assert.Equal(42, Assert.Single(observation.Buckets).Primary!.UsedPercent);
    }

    [Fact]
    public async Task Malformed_jsonl_before_a_plausible_reply_fails_closed()
    {
        var reply = """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":42}}}}}""";
        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(
            Step(1, InitializeOkResponse),
            new AppServerStepScript(2, ["{not-json", reply])));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task Two_distinct_replies_for_the_same_request_id_fail_closed_to_unknown()
    {
        var replyA = """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":1,"windowDurationMins":300,"resetsAt":1790875200}}}}}""";
        var replyB = """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":99,"windowDurationMins":300,"resetsAt":1790875200}}}}}""";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            new AppServerStepScript(2, [replyA, replyB])));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task The_same_reply_repeated_for_the_same_request_id_is_still_trusted()
    {
        var reply = """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":42,"windowDurationMins":300,"resetsAt":1790875200}}}}}""";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            new AppServerStepScript(2, [reply, reply])));

        Assert.True(observation.IsObserved);
        Assert.Equal(42, Assert.Single(observation.Buckets).Primary!.UsedPercent);
    }

    [Fact]
    public async Task An_unterminated_line_that_keeps_growing_past_the_per_line_bound_is_ignored_without_buffering_without_limit()
    {
        var oversizedRaw = "{\"id\":1,\"result\":{\"padding\":\"" + new string('x', 64 * 1024);

        var adapter = CreateAdapter(invocationTimeout: TimeSpan.FromMilliseconds(500));
        var observation = await InvokeWithScriptAsync(adapter, new AppServerScript(
        [
            new AppServerStepScript(1, [InitializeOkResponse]),
            new AppServerStepScript(2, WriteRaw: oversizedRaw),
        ]));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task A_single_complete_line_already_longer_than_the_per_line_bound_is_discarded_and_scanning_resynchronizes()
    {
        // Unlike the unterminated case above, this line DOES end with a newline — so a bound
        // that only checked incomplete/split lines would wrongly parse it. The real reply
        // follows immediately on the next line, proving the scanner resynchronizes rather than
        // treating the rest of the stream as unusable.
        var oversizedLine = "{\"id\":1,\"padding\":\"" + new string('x', 20 * 1024) + "\"}";
        var realReply = """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":42,"windowDurationMins":300,"resetsAt":1790875200}}}}}""";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            new AppServerStepScript(2, [oversizedLine, realReply])));

        Assert.True(observation.IsObserved);
        Assert.Equal(42, Assert.Single(observation.Buckets).Primary!.UsedPercent);
    }

    [Fact]
    public async Task A_split_oversized_line_cannot_reinterpret_its_suffix_as_a_valid_reply()
    {
        // The scanner reads stdout in 8192-byte chunks. The old implementation cleared its
        // pending prefix after three chunks, then accepted this JSON suffix as a new line.
        var fakeSuffix = """{"id":1,"result":{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":99}}}}}""";
        var oversizedLine = new string('x', 3 * 8192) + fakeSuffix + "\n";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, new AppServerScript(
        [
            new AppServerStepScript(1, [InitializeOkResponse]),
            new AppServerStepScript(2, WriteRaw: oversizedLine),
        ]));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task A_json_rpc_error_on_the_read_method_reports_unknown()
    {
        var errorResponse = """{"id":1,"error":{"code":-32601,"message":"account/rateLimits/read requires experimentalApi capability"}}""";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, InitializeOkResponse),
            Step(2, errorResponse)));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task A_json_rpc_error_on_initialize_reports_unknown_without_attempting_the_read_method()
    {
        var errorResponse = """{"id":0,"error":{"code":-32600,"message":"Not initialized"}}""";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(
            Step(1, errorResponse)));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task An_initialize_reply_with_neither_result_nor_error_reports_unknown()
    {
        var malformedInitialize = """{"id":0,"unexpected":true}""";

        var adapter = CreateAdapter();
        var observation = await InvokeWithScriptAsync(adapter, Script(Step(1, malformedInitialize)));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task An_initialize_reply_with_a_non_object_result_reports_unknown()
    {
        var malformedInitialize = """{"id":0,"result":"not-an-initialize-result"}""";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(Step(1, malformedInitialize)));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task A_server_that_never_replies_times_out_to_unknown_and_the_child_process_tree_is_terminated()
    {
        var adapter = CreateAdapter(invocationTimeout: TimeSpan.FromMilliseconds(300));
        var pidFilePath = Path.Combine(_scratchDirectory, "hang-timeout.pid");
        var childPidFilePath = Path.Combine(_scratchDirectory, "hang-timeout-child.pid");
        var observation = await InvokeWithScriptAsync(adapter, new AppServerScript(
            [], ThenHang: true, PidFilePath: pidFilePath, ChildPidFilePath: childPidFilePath));

        Assert.False(observation.IsObserved);
        await AssertProcessRecordedInPidFileHasExitedAsync(pidFilePath);
        await AssertProcessRecordedInPidFileHasExitedAsync(childPidFilePath);
    }

    [Fact]
    public async Task Cancellation_propagates_and_the_child_process_tree_is_still_terminated()
    {
        var adapter = CreateAdapter();
        var pidFilePath = Path.Combine(_scratchDirectory, "hang-cancel.pid");
        var childPidFilePath = Path.Combine(_scratchDirectory, "hang-cancel-child.pid");
        var scriptPath = WriteScript(new AppServerScript(
            [], ThenHang: true, PidFilePath: pidFilePath, ChildPidFilePath: childPidFilePath));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.ObserveAsync(FixtureExecutablePath, scriptPath, cancellation.Token));

        await AssertProcessRecordedInPidFileHasExitedAsync(pidFilePath);
        await AssertProcessRecordedInPidFileHasExitedAsync(childPidFilePath);
    }

    [Fact]
    public async Task A_process_that_exits_immediately_with_an_unsupported_mode_reports_unknown()
    {
        var adapter = CreateAdapter();

        var observation = await adapter.ObserveAsync(FixtureExecutablePath, scriptPath: null, CancellationToken.None);

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task A_launch_target_that_no_longer_exists_reports_unknown_without_starting_a_process()
    {
        var adapter = CreateAdapter();
        var missingPath = Path.Combine(_scratchDirectory, "does-not-exist.exe");

        var observation = await adapter.ObserveAsync(missingPath, scriptPath: null, CancellationToken.None);

        Assert.False(observation.IsObserved);
    }

    /// <summary>Polls (bounded, a few hundred milliseconds at most — cleanup itself is finite)
    /// for the real OS process the fixture recorded its own id for to actually exit, proving the
    /// adapter's cleanup terminated the child process tree rather than merely returning while it
    /// keeps running.</summary>
    private static async Task AssertProcessRecordedInPidFileHasExitedAsync(string pidFilePath)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (File.Exists(pidFilePath))
            {
                break;
            }

            await Task.Delay(20);
        }

        Assert.True(File.Exists(pidFilePath), "Expected the fixture to have recorded its own process id.");
        var pid = int.Parse(File.ReadAllText(pidFilePath));

        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (!IsProcessRunning(pid))
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.False(IsProcessRunning(pid), $"Expected process {pid} to have been terminated by cleanup.");
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static CodexAccountAllowanceAdapter CreateAdapter(TimeSpan? invocationTimeout = null) =>
        new(TimeProvider.System, invocationTimeout);

    private async Task<DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexAccountAllowance.CodexAccountAllowanceObservation>
        InvokeWithScriptAsync(CodexAccountAllowanceAdapter adapter, AppServerScript script)
    {
        var scriptPath = WriteScript(script);
        return await adapter.ObserveAsync(FixtureExecutablePath, scriptPath, CancellationToken.None);
    }

    private string WriteScript(AppServerScript script)
    {
        var path = Path.Combine(_scratchDirectory, $"{Guid.NewGuid():N}.appserver-script.json");
        File.WriteAllText(path, JsonSerializer.Serialize(script));
        return path;
    }

    private static AppServerScript Script(params AppServerStepScript[] steps) => new([.. steps]);

    private static AppServerStepScript Step(int readLines, string writeLine) => new(readLines, [writeLine]);

    private sealed record AppServerScript(
        List<AppServerStepScript> Steps, bool ThenHang = false, string? PidFilePath = null,
        string? ChildPidFilePath = null, string? RequestLogPath = null);

    private sealed record AppServerStepScript(int ReadLines, List<string>? WriteLines = null, string? WriteRaw = null, int SleepMs = 0);
}
