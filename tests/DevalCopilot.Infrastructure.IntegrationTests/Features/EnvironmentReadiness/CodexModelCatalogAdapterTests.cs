using System.Diagnostics;
using System.Text.Json;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Exercises <see cref="CodexModelCatalogAdapter"/> against a real child process — the
/// deterministic <c>ProcessExecutionFixture</c> test double, driven through its Node-script launch
/// shape exactly like <see cref="CodexAccountAllowanceAdapterTests"/>, since the behavior under
/// test (the live duplex JSON-RPC handshake, bounded cursor paging, response correlation, and
/// process-tree cleanup) only exists at the real OS process boundary.
///
/// Every fixture response below uses the documented <c>model/list</c> response shape
/// (<c>https://learn.chatgpt.com/docs/app-server</c>): a <c>data</c> array of model entries, each
/// with <c>id</c>, <c>displayName</c>, optional <c>hidden</c>, optional
/// <c>supportedReasoningEfforts</c> (an array of <c>{reasoningEffort, description}</c> objects),
/// and optional <c>defaultReasoningEffort</c>; and a <c>nextCursor</c> string or <see
/// langword="null"/>.
/// </summary>
public sealed class CodexModelCatalogAdapterTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");

    private const string InitializeOkResponse = """{"id":0,"result":{}}""";

    private readonly string _scratchDirectory;

    public CodexModelCatalogAdapterTests()
    {
        _scratchDirectory = Path.Combine(Path.GetTempPath(), $"devalcopilot-model-catalog-tests-{Guid.NewGuid():N}");
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
    public async Task A_multi_page_response_combines_visible_models_with_supported_and_default_effort()
    {
        var page1 = """
            {"id":1,"result":{"data":[
                {"id":"gpt-6-sol","displayName":"GPT-6 Sol","hidden":false,"defaultReasoningEffort":"medium",
                 "supportedReasoningEfforts":[{"reasoningEffort":"medium","description":"Balanced"},{"reasoningEffort":"high","description":"Deep"}]}
            ],"nextCursor":"page-2-cursor"}}
            """.ReplaceLineEndings("");
        var page2 = """
            {"id":2,"result":{"data":[
                {"id":"gpt-6-mini","displayName":"GPT-6 Mini","supportedReasoningEfforts":[{"reasoningEffort":"low","description":"Fast"}]}
            ],"nextCursor":null}}
            """.ReplaceLineEndings("");

        var requestLogPath = Path.Combine(_scratchDirectory, "multi-page-requests.jsonl");
        var observation = await InvokeWithScriptAsync(CreateAdapter(), new AppServerScript(
        [
            Step(1, InitializeOkResponse),
            Step(2, page1),
            Step(1, page2),
        ], RequestLogPath: requestLogPath));

        Assert.True(observation.IsObserved);
        Assert.NotNull(observation.RetrievedAtUtc);
        Assert.Equal(2, observation.Models.Count);

        var solModel = Assert.Single(observation.Models, model => model.Id == "gpt-6-sol");
        Assert.Equal("GPT-6 Sol", solModel.DisplayName);
        Assert.Equal(["medium", "high"], solModel.SupportedReasoningEfforts);
        Assert.Equal("medium", solModel.DefaultReasoningEffort);

        var miniModel = Assert.Single(observation.Models, model => model.Id == "gpt-6-mini");
        Assert.Equal("GPT-6 Mini", miniModel.DisplayName);
        Assert.Equal(["low"], miniModel.SupportedReasoningEfforts);
        Assert.Null(miniModel.DefaultReasoningEffort);

        var loggedLines = File.ReadAllLines(requestLogPath);
        Assert.Equal(4, loggedLines.Length);
        Assert.Equal(
            """{"method":"initialize","id":0,"params":{"clientInfo":{"name":"DevalCopilot","title":"DevalCopilot","version":"1.0.0"},"capabilities":{}}}""",
            loggedLines[0]);
        Assert.Equal("""{"method":"initialized","params":{}}""", loggedLines[1]);
        // Exact outbound model/list request JSON, including the bounded page-size limit — this
        // is the closed, method-specific request shape CodexAppServerChannel itself builds; it is
        // never an arbitrary caller-supplied payload.
        Assert.Equal("""{"method":"model/list","id":1,"params":{"includeHidden":false,"limit":50}}""", loggedLines[2]);
        Assert.Equal(
            """{"method":"model/list","id":2,"params":{"includeHidden":false,"limit":50,"cursor":"page-2-cursor"}}""",
            loggedLines[3]);
    }

    [Fact]
    public async Task A_hidden_entry_is_excluded_from_the_projection_even_though_includeHidden_was_requested_false()
    {
        var response = """
            {"id":1,"result":{"data":[
                {"id":"visible-model","displayName":"Visible","hidden":false},
                {"id":"hidden-model","displayName":"Hidden","hidden":true}
            ],"nextCursor":null}}
            """.ReplaceLineEndings("");

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(Step(1, InitializeOkResponse), Step(2, response)));

        Assert.True(observation.IsObserved);
        var model = Assert.Single(observation.Models);
        Assert.Equal("visible-model", model.Id);
    }

    [Fact]
    public async Task An_empty_catalog_reports_unknown_rather_than_an_observed_snapshot_with_nothing_to_show()
    {
        var response = """{"id":1,"result":{"data":[],"nextCursor":null}}""";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(Step(1, InitializeOkResponse), Step(2, response)));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task A_missing_displayName_falls_back_to_the_model_id_without_failing_the_entry()
    {
        var response = """{"id":1,"result":{"data":[{"id":"gpt-6-sol"}],"nextCursor":null}}""";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(Step(1, InitializeOkResponse), Step(2, response)));

        Assert.True(observation.IsObserved);
        var model = Assert.Single(observation.Models);
        Assert.Equal("gpt-6-sol", model.DisplayName);
        Assert.NotNull(model.SupportedReasoningEfforts);
        Assert.Empty(model.SupportedReasoningEfforts);
        Assert.Null(model.DefaultReasoningEffort);
    }

    [Theory]
    [InlineData("Bad\u0007Name")]
    [InlineData("Bad\u202EName")]
    [InlineData("Bad\u200FName")]
    [InlineData("Bad\u061CName")]
    public async Task A_displayName_with_a_control_or_bidirectional_formatting_character_falls_back_to_the_model_id(string unsafeDisplayName)
    {
        // Serialized (not raw-injected) so the control/bidi character reaches the fixture as a
        // properly escaped JSON string value rather than an actual unescaped control byte in the
        // JSONL stream itself.
        var entryJson = JsonSerializer.Serialize(new { id = "gpt-6-sol", displayName = unsafeDisplayName });
        var response = "{\"id\":1,\"result\":{\"data\":[" + entryJson + "],\"nextCursor\":null}}";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(Step(1, InitializeOkResponse), Step(2, response)));

        Assert.True(observation.IsObserved);
        var model = Assert.Single(observation.Models);
        Assert.Equal("gpt-6-sol", model.DisplayName);
    }

    [Fact]
    public async Task A_model_id_that_fails_the_bounded_character_check_fails_the_whole_catalog_closed()
    {
        var response = """{"id":1,"result":{"data":[{"id":"<script>bad id","displayName":"Bad"}],"nextCursor":null}}""";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(Step(1, InitializeOkResponse), Step(2, response)));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task Duplicate_model_ids_across_pages_fail_closed_instead_of_choosing_one_value()
    {
        var page1 = """{"id":1,"result":{"data":[{"id":"gpt-6-sol","displayName":"First"}],"nextCursor":"next"}}""";
        var page2 = """{"id":2,"result":{"data":[{"id":"gpt-6-sol","displayName":"Second"}],"nextCursor":null}}""";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(
            Step(1, InitializeOkResponse), Step(2, page1), Step(1, page2)));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task A_non_array_supportedReasoningEfforts_shape_fails_the_entry_closed()
    {
        var response = """{"id":1,"result":{"data":[{"id":"gpt-6-sol","displayName":"Sol","supportedReasoningEfforts":"not-an-array"}],"nextCursor":null}}""";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(Step(1, InitializeOkResponse), Step(2, response)));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task An_invalid_individual_reasoningEffort_element_makes_the_whole_effort_list_unknown_rather_than_partial()
    {
        var response = """
            {"id":1,"result":{"data":[
                {"id":"gpt-6-sol","displayName":"Sol","supportedReasoningEfforts":[
                    {"reasoningEffort":"medium"},
                    {"noEffortField":true}
                ]}
            ],"nextCursor":null}}
            """.ReplaceLineEndings("");

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(Step(1, InitializeOkResponse), Step(2, response)));

        Assert.True(observation.IsObserved);
        var model = Assert.Single(observation.Models);
        Assert.Null(model.SupportedReasoningEfforts);
    }

    [Fact]
    public async Task A_duplicate_reasoningEffort_within_one_entry_makes_the_whole_effort_list_unknown()
    {
        var response = """
            {"id":1,"result":{"data":[
                {"id":"gpt-6-sol","displayName":"Sol","supportedReasoningEfforts":[
                    {"reasoningEffort":"medium"},
                    {"reasoningEffort":"medium"}
                ]}
            ],"nextCursor":null}}
            """.ReplaceLineEndings("");

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(Step(1, InitializeOkResponse), Step(2, response)));

        Assert.True(observation.IsObserved);
        var model = Assert.Single(observation.Models);
        Assert.Null(model.SupportedReasoningEfforts);
    }

    [Fact]
    public async Task A_default_effort_not_among_the_entrys_own_supported_efforts_projects_as_unknown()
    {
        var response = """
            {"id":1,"result":{"data":[
                {"id":"gpt-6-sol","displayName":"Sol","defaultReasoningEffort":"extreme",
                 "supportedReasoningEfforts":[{"reasoningEffort":"medium"},{"reasoningEffort":"high"}]}
            ],"nextCursor":null}}
            """.ReplaceLineEndings("");

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(Step(1, InitializeOkResponse), Step(2, response)));

        Assert.True(observation.IsObserved);
        var model = Assert.Single(observation.Models);
        Assert.Equal(["medium", "high"], model.SupportedReasoningEfforts);
        Assert.Null(model.DefaultReasoningEffort);
    }

    [Fact]
    public async Task A_default_effort_reported_alongside_an_unknown_supported_list_projects_as_unknown()
    {
        var response = """
            {"id":1,"result":{"data":[
                {"id":"gpt-6-sol","displayName":"Sol","defaultReasoningEffort":"medium",
                 "supportedReasoningEfforts":[{"reasoningEffort":"medium"},{"reasoningEffort":"medium"}]}
            ],"nextCursor":null}}
            """.ReplaceLineEndings("");

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(Step(1, InitializeOkResponse), Step(2, response)));

        Assert.True(observation.IsObserved);
        var model = Assert.Single(observation.Models);
        Assert.Null(model.SupportedReasoningEfforts);
        Assert.Null(model.DefaultReasoningEffort);
    }

    [Fact]
    public async Task A_provider_that_still_claims_more_pages_past_the_bounded_page_count_fails_closed()
    {
        var steps = new List<AppServerStepScript> { Step(1, InitializeOkResponse) };
        for (var page = 1; page <= 8; page++)
        {
            var response = "{\"id\":" + page + ",\"result\":{\"data\":[{\"id\":\"model-" + page
                + "\",\"displayName\":\"Model " + page + "\"}],\"nextCursor\":\"more\"}}";
            steps.Add(page == 1 ? Step(2, response) : Step(1, response));
        }

        var observation = await InvokeWithScriptAsync(CreateAdapter(), new AppServerScript(steps));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task Unsolicited_notifications_interleaved_with_the_real_reply_are_ignored()
    {
        var response = """{"id":1,"result":{"data":[{"id":"gpt-6-sol","displayName":"Sol"}],"nextCursor":null}}""";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(
            Step(1, InitializeOkResponse),
            new AppServerStepScript(2,
            [
                """{"method":"session/updated","params":{}}""",
                """{"method":"item/completed","params":{"noise":true}}""",
                response,
            ])));

        Assert.True(observation.IsObserved);
        Assert.Equal("gpt-6-sol", Assert.Single(observation.Models).Id);
    }

    [Fact]
    public async Task Malformed_jsonl_before_a_plausible_reply_fails_closed()
    {
        var reply = """{"id":1,"result":{"data":[{"id":"gpt-6-sol","displayName":"Sol"}],"nextCursor":null}}""";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(
            Step(1, InitializeOkResponse),
            new AppServerStepScript(2, ["{not-json", reply])));

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task Two_distinct_replies_for_the_same_request_id_fail_closed_to_unknown()
    {
        var replyA = """{"id":1,"result":{"data":[{"id":"gpt-6-sol","displayName":"Sol"}],"nextCursor":null}}""";
        var replyB = """{"id":1,"result":{"data":[{"id":"gpt-6-mini","displayName":"Mini"}],"nextCursor":null}}""";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(
            Step(1, InitializeOkResponse),
            new AppServerStepScript(2, [replyA, replyB])));

        Assert.False(observation.IsObserved);
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
    public async Task A_json_rpc_error_on_model_list_reports_unknown()
    {
        var errorResponse = """{"id":1,"error":{"code":-32601,"message":"model/list requires experimentalApi capability"}}""";

        var observation = await InvokeWithScriptAsync(CreateAdapter(), Script(Step(1, InitializeOkResponse), Step(2, errorResponse)));

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
        var observation = await CreateAdapter().ObserveAsync(FixtureExecutablePath, scriptPath: null, CancellationToken.None);

        Assert.False(observation.IsObserved);
    }

    [Fact]
    public async Task A_launch_target_that_no_longer_exists_reports_unknown_without_starting_a_process()
    {
        var missingPath = Path.Combine(_scratchDirectory, "does-not-exist.exe");

        var observation = await CreateAdapter().ObserveAsync(missingPath, scriptPath: null, CancellationToken.None);

        Assert.False(observation.IsObserved);
    }

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

    private static CodexModelCatalogAdapter CreateAdapter(TimeSpan? invocationTimeout = null) =>
        new(TimeProvider.System, invocationTimeout);

    private async Task<DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog.CodexModelCatalogObservation>
        InvokeWithScriptAsync(CodexModelCatalogAdapter adapter, AppServerScript script)
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
