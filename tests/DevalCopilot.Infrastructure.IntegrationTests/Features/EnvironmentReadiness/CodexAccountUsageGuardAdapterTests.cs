using System.Diagnostics;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// The strict account-usage observation (ADR-0025) against a real child process, the deterministic <c>ProcessExecutionFixture</c> double
/// driven through its script shape, because the behavior under test (the live duplex JSON-RPC handshake, correlation, bounded scanning,
/// process-tree cleanup) only exists at the real OS boundary. The reply shape is the one the installed Codex build's own generated
/// schema documents (<c>GetAccountRateLimitsResponse</c>): a required legacy <c>rateLimits</c> snapshot, an optional multi-bucket
/// <c>rateLimitsByLimitId</c>, windows with a required integer <c>usedPercent</c> and nullable <c>windowDurationMins</c>/<c>resetsAt</c>,
/// a nullable <c>rateLimitReachedType</c>, and unrelated credit data that must never change the answer. The observation is admitted
/// whole or not at all: every case below that is not a complete valid snapshot must be <see cref="AccountUsageObservation.Unavailable"/>,
/// never a healthy subset.
/// </summary>
public sealed class CodexAccountUsageGuardAdapterTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");
    private static readonly DateTimeOffset Instant = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private const string InitializeOkResponse = """{"id":0,"result":{}}""";

    private readonly string _scratchDirectory;

    public CodexAccountUsageGuardAdapterTests()
    {
        _scratchDirectory = Path.Combine(Path.GetTempPath(), $"devalcopilot-account-usage-guard-tests-{Guid.NewGuid():N}");
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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static string Reply(string resultJson) => "{\"id\":1,\"result\":" + resultJson + "}";

    private async Task<AccountUsageObservation> ObserveAsync(string readReply, string? requestLogPath = null, TimeSpan? timeout = null) =>
        await ObserveScriptAsync(new AppServerScript([Step(1, InitializeOkResponse), Step(2, readReply)], RequestLogPath: requestLogPath), timeout);

    private async Task<AccountUsageObservation> ObserveScriptAsync(AppServerScript script, TimeSpan? timeout = null)
    {
        var adapter = new CodexAccountUsageGuardAdapter(new FixedClock(Instant), timeout);
        return await adapter.ObserveAsync(FixtureExecutablePath, WriteScript(script), CancellationToken.None);
    }

    // ---- valid snapshots ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_official_shape_multi_bucket_reply_is_admitted_with_the_host_retrieval_instant_and_only_the_protocol_requests()
    {
        var log = Path.Combine(_scratchDirectory, "requests.jsonl");

        var observation = await ObserveAsync(Reply("""
            {"rateLimits":{"limitId":"codex","primary":{"usedPercent":1}},
             "rateLimitsByLimitId":{"codex":{"limitId":"codex","limitName":"Codex","primary":{"usedPercent":42,"windowDurationMins":300,"resetsAt":1790875200},
                                             "secondary":{"usedPercent":10,"windowDurationMins":10080,"resetsAt":1791417600},"rateLimitReachedType":null,"planType":"pro"}}}
            """.ReplaceLineEndings("")), log);

        Assert.True(observation.IsValid);
        Assert.Equal(Instant, observation.RetrievedAtUtc);
        var bucket = Assert.Single(observation.Buckets);
        Assert.Equal("codex", bucket.Id);
        Assert.Equal(new AccountUsageWindow(42, DateTimeOffset.FromUnixTimeSeconds(1790875200)), bucket.Primary);
        Assert.Equal(new AccountUsageWindow(10, DateTimeOffset.FromUnixTimeSeconds(1791417600)), bucket.Secondary);
        Assert.False(observation.ProviderReportedLimitReached);
        Assert.Equal(
        [
            """{"method":"initialize","id":0,"params":{"clientInfo":{"name":"DevalCopilot","title":"DevalCopilot","version":"1.0.0"},"capabilities":{}}}""",
            """{"method":"initialized","params":{}}""",
            """{"method":"account/rateLimits/read","id":1}""",
        ], File.ReadAllLines(log));
    }

    [Fact]
    public async Task Several_buckets_are_each_evaluated_on_their_own_and_never_merged_with_the_legacy_view()
    {
        var observation = await ObserveAsync(Reply("""
            {"rateLimits":{"primary":{"usedPercent":99}},
             "rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":5}},"gpt-5":{"secondary":{"usedPercent":6}}}}
            """.ReplaceLineEndings("")));

        Assert.True(observation.IsValid);
        Assert.Equal(["codex", "gpt-5"], observation.Buckets.Select(bucket => bucket.Id));
        Assert.DoesNotContain(observation.Buckets, bucket => bucket.Id is null);
        Assert.Equal(5, observation.Buckets[0].Primary!.UsedPercent);
        Assert.Equal(6, observation.Buckets[1].Secondary!.UsedPercent);
    }

    [Theory]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":30},"secondary":{"usedPercent":31}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":30},"secondary":{"usedPercent":31}},"rateLimitsByLimitId":null}""")]
    public async Task The_legacy_single_snapshot_is_used_only_when_the_multi_bucket_view_is_absent_or_null(string result)
    {
        var observation = await ObserveAsync(Reply(result));

        Assert.True(observation.IsValid);
        var bucket = Assert.Single(observation.Buckets);
        Assert.Null(bucket.Id);
        Assert.Equal(30, bucket.Primary!.UsedPercent);
        Assert.Equal(31, bucket.Secondary!.UsedPercent);
    }

    [Theory]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":0},"secondary":null}}""", 0, null)]
    [InlineData("""{"rateLimits":{"primary":null,"secondary":{"usedPercent":100}}}""", null, 100)]
    [InlineData("""{"rateLimits":{"secondary":{"usedPercent":100,"windowDurationMins":0,"resetsAt":null}}}""", null, 100)]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":7,"windowDurationMins":null}}}""", 7, null)]
    public async Task A_null_or_absent_window_is_the_documented_absence_when_another_valid_window_exists(string result, int? primary, int? secondary)
    {
        var observation = await ObserveAsync(Reply(result));

        Assert.True(observation.IsValid);
        var bucket = Assert.Single(observation.Buckets);
        Assert.Equal(primary, bucket.Primary?.UsedPercent);
        Assert.Equal(secondary, bucket.Secondary?.UsedPercent);
    }

    [Theory]
    [InlineData("\"rate_limit_reached\"")]
    [InlineData("\"workspace_member_usage_limit_reached\"")]
    [InlineData("\"some-future-classification\"")]
    public async Task A_non_null_reached_limit_state_is_carried_without_its_text_whatever_the_percentages_are(string reachedType)
    {
        var observation = await ObserveAsync(Reply(
            "{\"rateLimits\":{\"primary\":{\"usedPercent\":1},\"rateLimitReachedType\":" + reachedType + "}}"));

        Assert.True(observation.IsValid);
        Assert.True(observation.ProviderReportedLimitReached);
        Assert.Equal(1, Assert.Single(observation.Buckets).Primary!.UsedPercent);
    }

    [Fact]
    public async Task One_reached_bucket_among_healthy_ones_sets_the_flag()
    {
        var observation = await ObserveAsync(Reply("""
            {"rateLimits":{"primary":{"usedPercent":1}},
             "rateLimitsByLimitId":{"a":{"primary":{"usedPercent":1}},"b":{"primary":{"usedPercent":1},"rateLimitReachedType":"rate_limit_reached"}}}
            """.ReplaceLineEndings("")));

        Assert.True(observation.ProviderReportedLimitReached);
        Assert.Equal(2, observation.Buckets.Length);
    }

    [Fact]
    public async Task Credit_data_never_changes_the_answer()
    {
        var observation = await ObserveAsync(Reply("""
            {"rateLimits":{"primary":{"usedPercent":90},"credits":{"hasCredits":true,"unlimited":true,"balance":"999999","unexpected":[1,2,{"x":1,"x":2}]}},
             "rateLimitUpsell":{"a":1,"a":2},"ordinaryUsageAllowed":true,"accountId":"acct_1"}
            """.ReplaceLineEndings("")));

        Assert.True(observation.IsValid);
        Assert.Equal(90, Assert.Single(observation.Buckets).Primary!.UsedPercent);
        Assert.False(observation.ProviderReportedLimitReached);
    }

    [Fact]
    public async Task Sixteen_buckets_are_admitted_inclusively_and_notifications_before_the_reply_are_ignored()
    {
        var entries = string.Join(',', Enumerable.Range(0, 16).Select(index => $"\"bucket-{index}\":{{\"primary\":{{\"usedPercent\":{index}}}}}"));
        var script = new AppServerScript(
        [
            Step(1, InitializeOkResponse),
            new AppServerStepScript(2, ["""{"method":"account/rateLimits/updated","params":{"x":1}}""", Reply("{\"rateLimits\":{\"primary\":{\"usedPercent\":1}},\"rateLimitsByLimitId\":{" + entries + "}}")]),
        ]);

        var observation = await ObserveScriptAsync(script);

        Assert.True(observation.IsValid);
        Assert.Equal(16, observation.Buckets.Length);
    }

    // ---- never a partial subset -----------------------------------------------------------------------------------------------

    [Theory]
    // a malformed sibling window beside a healthy one in the same bucket
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10},"secondary":{"usedPercent":101}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10},"secondary":{"usedPercent":-1}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10},"secondary":{}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10},"secondary":"x"}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10},"secondary":[]}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10},"secondary":5}}""")]
    // a malformed bucket beside a healthy bucket
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{"a":{"primary":{"usedPercent":10}},"b":{"primary":{"usedPercent":"x"}}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{"a":{"primary":{"usedPercent":10}},"b":"not-a-snapshot"}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{"a":{"primary":{"usedPercent":10}},"b":{"primary":null,"secondary":null}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{"a":{"primary":{"usedPercent":10}},"b":{}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{"a":{"primary":{"usedPercent":10}},"bad id":{"primary":{"usedPercent":1}}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{"a":{"primary":{"usedPercent":10}},"":{"primary":{"usedPercent":1}}}}""")]
    public async Task One_malformed_window_or_bucket_makes_the_whole_observation_unavailable_even_beside_healthy_data(string result)
    {
        var observation = await ObserveAsync(Reply(result));

        Assert.Same(AccountUsageObservation.Unavailable, observation);
    }

    [Theory]
    [InlineData("""{"primary":{"usedPercent":10.5}}""")]
    [InlineData("""{"primary":{"usedPercent":1e1}}""")]
    [InlineData("""{"primary":{"usedPercent":"10"}}""")]
    [InlineData("""{"primary":{"usedPercent":null}}""")]
    [InlineData("""{"primary":{"usedPercent":true}}""")]
    [InlineData("""{"primary":{"usedPercent":101}}""")]
    [InlineData("""{"primary":{"usedPercent":-1}}""")]
    [InlineData("""{"primary":{"usedPercent":2147483648}}""")]
    [InlineData("""{"primary":{"usedPercent":99999999999999999999}}""")]
    [InlineData("""{"primary":{"windowDurationMins":300}}""")]
    [InlineData("""{"primary":{"usedPercent":10,"windowDurationMins":-1}}""")]
    [InlineData("""{"primary":{"usedPercent":10,"windowDurationMins":"300"}}""")]
    [InlineData("""{"primary":{"usedPercent":10,"windowDurationMins":1.5}}""")]
    [InlineData("""{"primary":{"usedPercent":10,"windowDurationMins":true}}""")]
    [InlineData("""{"primary":{"usedPercent":10,"resetsAt":"2026-10-05T00:00:00Z"}}""")]
    [InlineData("""{"primary":{"usedPercent":10,"resetsAt":1.5}}""")]
    [InlineData("""{"primary":{"usedPercent":10,"resetsAt":99999999999999}}""")]
    [InlineData("""{"primary":{"usedPercent":10,"resetsAt":-99999999999999}}""")]
    [InlineData("""{"primary":{"usedPercent":10,"resetsAt":{}}}""")]
    [InlineData("""{"primary":{"usedPercent":10},"rateLimitReachedType":5}""")]
    [InlineData("""{"primary":{"usedPercent":10},"rateLimitReachedType":{}}""")]
    [InlineData("""{"primary":{"usedPercent":10},"rateLimitReachedType":true}""")]
    [InlineData("""{"primary":null,"secondary":null}""")]
    [InlineData("""{}""")]
    public async Task An_invalid_present_field_is_never_normalized_into_absence(string legacySnapshot)
    {
        var observation = await ObserveAsync(Reply("{\"rateLimits\":" + legacySnapshot + "}"));

        Assert.Same(AccountUsageObservation.Unavailable, observation);
    }

    [Theory]
    [InlineData("""{"rateLimits":"x"}""")]
    [InlineData("""{"rateLimits":null}""")]
    [InlineData("""{"rateLimits":[]}""")]
    [InlineData("""{}""")]
    [InlineData("""{"rateLimitsByLimitId":null}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":[]}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":"x"}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":5}""")]
    public async Task An_invalid_or_empty_multi_bucket_map_never_falls_back_to_the_legacy_view_and_a_missing_legacy_is_unavailable(string result)
    {
        var observation = await ObserveAsync(Reply(result));

        Assert.Same(AccountUsageObservation.Unavailable, observation);
    }

    [Fact]
    public async Task More_than_sixteen_buckets_or_an_over_long_identifier_is_unavailable()
    {
        var entries = string.Join(',', Enumerable.Range(0, 17).Select(index => $"\"bucket-{index}\":{{\"primary\":{{\"usedPercent\":{index}}}}}"));
        var longId = new string('a', 65);

        var over = await ObserveAsync(Reply("{\"rateLimits\":{\"primary\":{\"usedPercent\":1}},\"rateLimitsByLimitId\":{" + entries + "}}"));
        var tooLong = await ObserveAsync(Reply("{\"rateLimits\":{\"primary\":{\"usedPercent\":1}},\"rateLimitsByLimitId\":{\"" + longId + "\":{\"primary\":{\"usedPercent\":1}}}}"));
        var atLimit = await ObserveAsync(Reply("{\"rateLimits\":{\"primary\":{\"usedPercent\":1}},\"rateLimitsByLimitId\":{\"" + new string('a', 64) + "\":{\"primary\":{\"usedPercent\":1}}}}"));

        Assert.False(over.IsValid);
        Assert.False(tooLong.IsValid);
        Assert.True(atLimit.IsValid);
    }

    // ---- duplicated relevant properties ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10,"usedPercent":10}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10,"usedPercent":99}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10},"primary":{"usedPercent":10}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10},"secondary":{"usedPercent":1},"secondary":{"usedPercent":2}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10},"rateLimitReachedType":null,"rateLimitReachedType":"rate_limit_reached"}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10}},"rateLimits":{"primary":{"usedPercent":99}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{"a":{"primary":{"usedPercent":10}}},"rateLimitsByLimitId":{"a":{"primary":{"usedPercent":10}}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{"a":{"primary":{"usedPercent":10}},"a":{"primary":{"usedPercent":99}}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{"a":{"primary":{"usedPercent":10}},"%u0061":{"primary":{"usedPercent":99}}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":10,"%u0075sedPercent":99}}}""")]
    public async Task A_duplicated_relevant_property_or_bucket_key_makes_the_whole_observation_unavailable(string result)
    {
        // A JSON unicode escape names the same property as its literal spelling: %u stands for a backslash and a u.
        result = result.Replace("%u", ((char)92).ToString() + "u", StringComparison.Ordinal);

        var observation = await ObserveAsync(Reply(result));

        Assert.Same(AccountUsageObservation.Unavailable, observation);
    }

    [Fact]
    public async Task A_duplicated_result_or_id_member_of_the_reply_itself_is_unavailable()
    {
        var duplicateResult = await ObserveAsync("{\"id\":1,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":1}}},\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":99}}}}");

        Assert.False(duplicateResult.IsValid);
    }

    [Fact]
    public async Task Two_different_replies_for_the_one_request_are_never_resolved_by_picking_one()
    {
        var script = new AppServerScript(
        [
            Step(1, InitializeOkResponse),
            new AppServerStepScript(2, [Reply("""{"rateLimits":{"primary":{"usedPercent":1}}}"""), Reply("""{"rateLimits":{"primary":{"usedPercent":99}}}""")]),
        ]);

        Assert.False((await ObserveScriptAsync(script)).IsValid);
    }

    // ---- failed exchanges -----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("""{"id":1,"error":{"code":-32601,"message":"Method not found"}}""")]
    [InlineData("""{"id":1,"result":"not-an-object"}""")]
    [InlineData("""{"id":1,"result":null}""")]
    [InlineData("""{"id":1}""")]
    [InlineData("""{"id":1,"result":{"rateLimits":{"primary":{"usedPercent":1}}},"error":{}}""")]
    [InlineData("not json at all")]
    [InlineData("""[1,2,3]""")]
    public async Task An_error_a_non_object_result_or_a_malformed_reply_is_unavailable(string reply)
    {
        Assert.Same(AccountUsageObservation.Unavailable, await ObserveAsync(reply));
    }

    [Fact]
    public async Task A_reply_line_over_the_line_bound_or_a_truncated_capture_is_unavailable()
    {
        var padding = new string('x', 17 * 1024);
        var oversizedLine = Reply("{\"rateLimits\":{\"primary\":{\"usedPercent\":1}},\"rateLimitUpsell\":\"" + padding + "\"}");
        var truncated = new AppServerScript(
        [
            Step(1, InitializeOkResponse),
            new AppServerStepScript(2, null, WriteRaw: "{\"id\":1,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":1}}"),
        ]);

        Assert.False((await ObserveAsync(oversizedLine)).IsValid);
        Assert.False((await ObserveScriptAsync(truncated, TimeSpan.FromMilliseconds(400))).IsValid);
    }

    [Fact]
    public async Task A_failed_handshake_is_unavailable()
    {
        var script = new AppServerScript([Step(1, """{"id":0,"error":{"code":-1,"message":"no"}}""")]);

        Assert.False((await ObserveScriptAsync(script)).IsValid);
    }

    [Fact]
    public async Task A_server_that_never_replies_times_out_to_unavailable_and_the_child_process_tree_is_terminated()
    {
        var pidFile = Path.Combine(_scratchDirectory, "hang-timeout.pid");
        var childPidFile = Path.Combine(_scratchDirectory, "hang-timeout-child.pid");

        var observation = await ObserveScriptAsync(
            new AppServerScript([], ThenHang: true, PidFilePath: pidFile, ChildPidFilePath: childPidFile), TimeSpan.FromMilliseconds(300));

        Assert.Same(AccountUsageObservation.Unavailable, observation);
        await AssertProcessHasExitedAsync(pidFile);
        await AssertProcessHasExitedAsync(childPidFile);
    }

    [Fact]
    public async Task Cancellation_propagates_and_the_child_process_tree_is_still_terminated()
    {
        var pidFile = Path.Combine(_scratchDirectory, "hang-cancel.pid");
        var childPidFile = Path.Combine(_scratchDirectory, "hang-cancel-child.pid");
        var scriptPath = WriteScript(new AppServerScript([], ThenHang: true, PidFilePath: pidFile, ChildPidFilePath: childPidFile));
        var adapter = new CodexAccountUsageGuardAdapter(TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.ObserveAsync(FixtureExecutablePath, scriptPath, cancellation.Token));

        await AssertProcessHasExitedAsync(pidFile);
        await AssertProcessHasExitedAsync(childPidFile);
    }

    [Fact]
    public async Task An_unsupported_launch_target_or_a_missing_one_is_unavailable_without_a_process()
    {
        var adapter = new CodexAccountUsageGuardAdapter(TimeProvider.System);

        Assert.False((await adapter.ObserveAsync(FixtureExecutablePath, null, CancellationToken.None)).IsValid);
        Assert.False((await adapter.ObserveAsync(Path.Combine(_scratchDirectory, "missing.exe"), null, CancellationToken.None)).IsValid);
    }

    // ---- why this adapter exists: the display adapter would keep the healthy subset ------------------------------------------------

    [Fact]
    public async Task The_display_only_allowance_adapter_keeps_a_healthy_subset_that_this_strict_adapter_refuses()
    {
        const string reply = """
            {"id":1,"result":{"rateLimits":{"primary":{"usedPercent":1}},
             "rateLimitsByLimitId":{"a":{"primary":{"usedPercent":10}},"b":{"primary":{"usedPercent":"x"}}}}}
            """;
        var script = new AppServerScript([Step(1, InitializeOkResponse), Step(2, reply.ReplaceLineEndings(""))]);

        var display = await new CodexAccountAllowanceAdapter(TimeProvider.System)
            .ObserveAsync(FixtureExecutablePath, WriteScript(script), CancellationToken.None);
        var strict = await ObserveScriptAsync(script);

        Assert.True(display.IsObserved);
        Assert.False(strict.IsValid);
    }

    [Fact]
    public async Task Each_observation_is_independent_with_its_own_retrieval_instant_and_nothing_is_cached()
    {
        var clock = new MovableClock(Instant);
        var adapter = new CodexAccountUsageGuardAdapter(clock);
        var path = WriteScript(new AppServerScript([Step(1, InitializeOkResponse), Step(2, Reply("""{"rateLimits":{"primary":{"usedPercent":3}}}"""))]));

        var first = await adapter.ObserveAsync(FixtureExecutablePath, path, CancellationToken.None);
        clock.Now = Instant.AddSeconds(7);
        var second = await adapter.ObserveAsync(FixtureExecutablePath, path, CancellationToken.None);

        Assert.Equal(Instant, first.RetrievedAtUtc);
        Assert.Equal(Instant.AddSeconds(7), second.RetrievedAtUtc);
        Assert.NotSame(first, second);
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task AssertProcessHasExitedAsync(string pidFilePath)
    {
        for (var attempt = 0; attempt < 50 && !File.Exists(pidFilePath); attempt++)
        {
            await Task.Delay(20);
        }

        Assert.True(File.Exists(pidFilePath), "Expected the fixture to have recorded its own process id.");
        var pid = int.Parse(File.ReadAllText(pidFilePath));
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (!IsRunning(pid))
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.False(IsRunning(pid), $"Expected process {pid} to have been terminated by cleanup.");
    }

    private static bool IsRunning(int pid)
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

    private string WriteScript(AppServerScript script)
    {
        var path = Path.Combine(_scratchDirectory, $"{Guid.NewGuid():N}.appserver-script.json");
        File.WriteAllText(path, JsonSerializer.Serialize(script));
        return path;
    }

    private static AppServerStepScript Step(int readLines, string writeLine) => new(readLines, [writeLine]);

    private sealed record AppServerScript(
        List<AppServerStepScript> Steps, bool ThenHang = false, string? PidFilePath = null,
        string? ChildPidFilePath = null, string? RequestLogPath = null);

    private sealed record AppServerStepScript(int ReadLines, List<string>? WriteLines = null, string? WriteRaw = null, int SleepMs = 0);
}
