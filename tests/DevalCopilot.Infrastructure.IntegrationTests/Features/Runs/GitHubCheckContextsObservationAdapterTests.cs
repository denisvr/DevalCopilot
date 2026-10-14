using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// The GitHub check-context observer (ADR-0045). Request shape, environment, the strict whole-response admission of the bounded
/// collection (every member at every level, the count and page-flag consistency, the literal enumerations, the opaque identifiers and the
/// label text contract with its single decode), single-flight and fault propagation are proved against a recording process double; the
/// real process boundary (argv, environment, the 128 KiB per-stream and 256 KiB combined caps, timeout, cancellation and cleanup) against
/// the isolated <c>ProcessExecutionFixture</c> "api" double. A fixture proves the process boundary only: it never proves how the installed
/// GitHub CLI formats a GraphQL answer, how stored authentication behaves or that GitHub is reliable. No real <c>gh</c>, account,
/// credential or network is ever used. No JSON escape sequence is written literally in this source: they are built from code points.
/// </summary>
public sealed partial class GitHubCheckContextsObservationAdapterTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");
    private const string GhPath = @"C:\safe\gh.exe";
    private const string Owner = "octo-cat";
    private const string Repository = "hello.world";
    private const string Branch = "devalcopilot/workspace/0123456789abcdef0123456789abcdef/3";
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string NodeId = "R_kgDOExample01";

    // Restated independently of the production constant: the exact read-only query the child must be asked for.
    private const string ExpectedQuery =
        "query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner "
        + "ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid statusCheckRollup{contexts(first:50)"
        + "{totalCount pageInfo{hasNextPage} nodes{__typename ... on CheckRun{id name status conclusion checkSuite{id}} "
        + "... on StatusContext{id context state}}}}}}}}}";

    private const int StreamCap = 128 * 1024;
    private const int TotalCap = 256 * 1024;

    private readonly string _scratch;

    public GitHubCheckContextsObservationAdapterTests()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"devalcopilot-gh-contexts-tests-{Guid.NewGuid():N}");
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

    // ---- JSON builders. Escape sequences are assembled from code points so no tool or serializer can rewrite them. ----

    private static string U(int codeUnit) => "\\" + "u" + codeUnit.ToString("x4");

    private static string Backslash(char c) => "\\" + c;

    private static string C(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static string Str(string value) => "\"" + value + "\"";

    private static string Raw(string? value) => value is null ? "null" : Str(value);

    private static string RunNode(
        string id = "CR_1", string name = "build", string status = "COMPLETED", string? conclusion = "SUCCESS", string suite = "CS_1") =>
        "{\"__typename\":\"CheckRun\",\"id\":" + Str(id) + ",\"name\":" + Str(name) + ",\"status\":" + Str(status)
        + ",\"conclusion\":" + Raw(conclusion) + ",\"checkSuite\":{\"id\":" + Str(suite) + "}}";

    private static string StatusNode(string id = "SC_1", string context = "ci/legacy", string state = "SUCCESS") =>
        "{\"__typename\":\"StatusContext\",\"id\":" + Str(id) + ",\"context\":" + Str(context) + ",\"state\":" + Str(state) + "}";

    private static string Nodes(params string[] nodes) => "[" + string.Join(",", nodes) + "]";

    private static string Contexts(string totalCount, string hasNextPage, string nodes) =>
        "{\"contexts\":{\"totalCount\":" + totalCount + ",\"pageInfo\":{\"hasNextPage\":" + hasNextPage + "},\"nodes\":" + nodes + "}}";

    private static string Rollup(params string[] nodes) =>
        Contexts(nodes.Length.ToString(), "false", Nodes(nodes));

    private static string RefJson(
        string name = Branch, string prefix = "refs/heads/", string typeName = "Commit", string oid = Sha, string rollup = "null") =>
        "{\"name\":" + Str(name) + ",\"prefix\":" + Str(prefix) + ",\"target\":{\"__typename\":" + Str(typeName) + ",\"oid\":" + Str(oid)
        + ",\"statusCheckRollup\":" + rollup + "}}";

    private static string Answer(
        string id = NodeId, string name = Repository, string? nameWithOwner = null, string? reference = null) =>
        "{\"data\":{\"repository\":{\"id\":" + Str(id) + ",\"name\":" + Str(name) + ",\"nameWithOwner\":"
        + Str(nameWithOwner ?? $"{Owner}/{name}") + ",\"ref\":" + (reference ?? RefJson()) + "}}}\n";

    private static string WithRollup(string rollup) => Answer(reference: RefJson(rollup: rollup));

    private static string WithNodes(params string[] nodes) => WithRollup(Rollup(nodes));

    private static string WithRef(string reference) => Answer(reference: reference);

    private static string WithTarget(string target) =>
        WithRef("{\"name\":" + Str(Branch) + ",\"prefix\":\"refs/heads/\",\"target\":" + target + "}");

    /// <summary>One answer that contains every property the query selects at every level, with both node kinds.</summary>
    private static string Literal() => WithNodes(RunNode(id: "CR_1"), StatusNode(id: "SC_1"), RunNode(id: "CR_2", conclusion: null));

    private GitHubCheckContextsObservationAdapter Create(
        IProcessExecutionAdapter process, Func<string, string?>? environment = null, TimeSpan? timeout = null) =>
        new(process, () => _scratch, environment ?? (_ => null), timeout);

    private Task<GitHubCheckContextsProbe?> ObserveStdoutAsync(
        string stdout, int exitCode = 0, string owner = Owner, string repository = Repository, string branch = Branch)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(stdout, exitCode)));
        return Create(process).ObserveAsync(GhPath, owner, repository, branch, CancellationToken.None);
    }

    private static GitHubCheckContext Run(
        string id = "CR_1", string name = "build", string status = "COMPLETED", string? conclusion = "SUCCESS", string suite = "CS_1") =>
        new("CheckRun", id, name, status, conclusion, suite);

    private static GitHubCheckContext Status(string id = "SC_1", string name = "ci/legacy", string state = "SUCCESS") =>
        new("StatusContext", id, name, state, null, null);

    private static void AssertPage(GitHubCheckContextsProbe? probe, int total, params GitHubCheckContext[] expected)
    {
        Assert.NotNull(probe);
        Assert.Equal(new GitHubCheckContextsRef(Branch, Sha), probe.Ref);
        Assert.NotNull(probe.Page);
        Assert.Equal(total, probe.Page.TotalCount);
        Assert.Equal(expected, probe.Page.Contexts);
    }

    // ---- Request shape, names, branch and environment. ----

    [Fact]
    public async Task The_request_is_one_literal_argv_with_raw_string_variables_inside_the_host_scratch_with_the_enlarged_caps_and_no_sinks()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(Answer())));

        await Create(process).ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None);

        var request = Assert.Single(process.Requests);
        Assert.Equal(GhPath, request.ExecutablePath);
        Assert.Equal(
            [
                "api", "graphql", "--hostname", "github.com", "--method", "POST",
                "-f", "query=" + ExpectedQuery, "-f", $"owner={Owner}", "-f", $"name={Repository}", "-f", $"ref=refs/heads/{Branch}",
            ],
            request.Arguments);
        Assert.Equal(ExpectedQuery, GitHubCheckContextsObservationAdapter.Query);
        Assert.Equal(_scratch, request.WorkingDirectory);
        Assert.Equal(_scratch, request.ApprovedRoot);
        Assert.Equal(TimeSpan.FromSeconds(15), request.Timeout);
        Assert.Equal(128 * 1024, request.MaxBytesPerStream);
        Assert.Equal(256 * 1024, request.MaxTotalCapturedBytes);
        Assert.Null(request.StandardOutputSinkPath);
        Assert.Null(request.StandardErrorSinkPath);
        Assert.Null(request.StandardInput);
        Assert.DoesNotContain("-F", request.Arguments);
        Assert.DoesNotContain("--jq", request.Arguments);
        Assert.DoesNotContain("--paginate", request.Arguments);
        Assert.DoesNotContain("--include", request.Arguments);
    }

    [Fact]
    public void The_query_is_one_constant_read_only_document_selecting_only_the_first_fifty_contexts_and_their_listed_fields()
    {
        var query = GitHubCheckContextsObservationAdapter.Query;

        Assert.StartsWith("query(", query, StringComparison.Ordinal);
        Assert.DoesNotContain("mutation", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contexts(first:50)", query, StringComparison.Ordinal);
        Assert.DoesNotContain("after:", query, StringComparison.Ordinal);
        Assert.DoesNotContain("endCursor", query, StringComparison.Ordinal);
        foreach (var forbidden in new[]
                 {
                     "app", "workflowRun", "detailsUrl", "url", "logs", "annotations", "steps", "pullRequest", "databaseId",
                     "startedAt", "completedAt", "title", "summary", "text", "viewer", "checkSuites", "checkRuns",
                 })
        {
            // Whole identifiers only: the fixed names "contexts" and "StatusContext" legitimately contain the letters "text".
            Assert.DoesNotMatch(new Regex($@"\b{forbidden}\b"), query);
        }

        var selected = Regex.Matches(query, @"[A-Za-z_]+").Select(match => match.Value).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            [
                "CheckRun", "Commit", "StatusContext", "String", "__typename", "checkSuite", "conclusion", "context", "contexts",
                "first", "hasNextPage", "id", "name", "nameWithOwner", "nodes", "oid", "on", "owner", "pageInfo", "prefix",
                "qualifiedName", "query", "ref", "repository", "state", "status", "statusCheckRollup", "target", "totalCount",
            ],
            selected);
        Assert.Equal(query.Count(c => c == '{'), query.Count(c => c == '}'));
    }

    [Theory]
    [InlineData("123", "456")]
    [InlineData("octo-cat", "repo.git")]
    [InlineData("o", "a.b_c-d.git")]
    public async Task Numeric_looking_names_and_a_git_suffix_are_passed_exactly_as_stored_and_never_interpolated(string owner, string repository)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(
            Result(Answer(name: repository, nameWithOwner: owner + "/" + repository))));

        var probe = await Create(process).ObserveAsync(GhPath, owner, repository, Branch, CancellationToken.None);

        var arguments = Assert.Single(process.Requests).Arguments;
        Assert.Equal(["-f", $"owner={owner}", "-f", $"name={repository}", "-f", $"ref=refs/heads/{Branch}"], arguments.Skip(arguments.Count - 6));
        Assert.Equal("query=" + ExpectedQuery, arguments[7]);
        Assert.NotNull(probe);
    }

    [Theory]
    [InlineData("", "repo")]
    [InlineData("owner", "")]
    [InlineData("-owner", "repo")]
    [InlineData("owner", ".repo")]
    [InlineData("owner", "re/po")]
    [InlineData("owner", "repo\"}){x")]
    [InlineData("own_er", "repo")]
    public async Task A_stored_name_outside_the_supported_subset_is_never_passed_to_a_process(string owner, string repository)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(Answer())));

        Assert.Null(await Create(process).ObserveAsync(GhPath, owner, repository, Branch, CancellationToken.None));
        Assert.Empty(process.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("main")]
    [InlineData("refs/heads/main")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/0")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/03")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3/4")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/2147483648")]
    [InlineData("devalcopilot/workspace/0123456789ABCDEF0123456789abcdef/3")]
    [InlineData("Devalcopilot/workspace/0123456789abcdef0123456789abcdef/3")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3\n")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3\"")]
    public async Task A_branch_outside_the_bounded_owned_form_is_never_passed_to_a_process(string branch)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(Answer())));

        Assert.Null(await Create(process).ObserveAsync(GhPath, Owner, Repository, branch, CancellationToken.None));
        Assert.Empty(process.Requests);
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
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(Answer())));

        await Create(process, name => ambient.GetValueOrDefault(name))
            .ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None);

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

    // ---- Admitted collections. ----

    [Fact]
    public async Task A_mixed_collection_is_admitted_in_provider_order_with_repeated_names_shared_suites_and_independent_literal_values()
    {
        var probe = await ObserveStdoutAsync(WithNodes(
            RunNode(id: "CR_a", name: "build", status: "COMPLETED", conclusion: "SUCCESS", suite: "CS_1"),
            RunNode(id: "CR_b", name: "build", status: "COMPLETED", conclusion: "FAILURE", suite: "CS_1"),
            StatusNode(id: "SC_a", context: "build", state: "PENDING"),
            RunNode(id: "CR_c", name: "deploy", status: "IN_PROGRESS", conclusion: null, suite: "CS_2"),
            RunNode(id: "CR_d", name: "lint", status: "COMPLETED", conclusion: null, suite: "CS_2"),
            RunNode(id: "CR_e", name: "e2e", status: "QUEUED", conclusion: "SUCCESS", suite: "CS_3"),
            StatusNode(id: "SC_b", context: "ci/legacy", state: "EXPECTED")));

        AssertPage(
            probe, 7,
            Run("CR_a", "build", "COMPLETED", "SUCCESS", "CS_1"), Run("CR_b", "build", "COMPLETED", "FAILURE", "CS_1"),
            Status("SC_a", "build", "PENDING"), Run("CR_c", "deploy", "IN_PROGRESS", null, "CS_2"),
            Run("CR_d", "lint", "COMPLETED", null, "CS_2"), Run("CR_e", "e2e", "QUEUED", "SUCCESS", "CS_3"),
            Status("SC_b", "ci/legacy", "EXPECTED"));
        Assert.Equal(NodeId, probe!.NodeId);
        Assert.Equal((Owner, Repository), (probe.Owner, probe.Repository));
    }

    [Fact]
    public async Task Every_check_run_status_conclusion_and_status_context_state_is_admitted_exactly_as_returned_with_no_pairing_rule()
    {
        var statuses = new[] { "COMPLETED", "IN_PROGRESS", "PENDING", "QUEUED", "REQUESTED", "WAITING" };
        var conclusions = new string?[]
        {
            null, "ACTION_REQUIRED", "CANCELLED", "FAILURE", "NEUTRAL", "SKIPPED", "STALE", "STARTUP_FAILURE", "SUCCESS", "TIMED_OUT",
        };
        var states = new[] { "ERROR", "EXPECTED", "FAILURE", "PENDING", "SUCCESS" };
        var nodes = new List<string>();
        var expected = new List<GitHubCheckContext>();
        for (var index = 0; index < statuses.Length; index++)
        {
            var conclusion = conclusions[index];
            nodes.Add(RunNode(id: $"CR_{index}", status: statuses[index], conclusion: conclusion));
            expected.Add(Run($"CR_{index}", "build", statuses[index], conclusion));
        }

        for (var index = 0; index < conclusions.Length; index++)
        {
            nodes.Add(RunNode(id: $"CX_{index}", status: "COMPLETED", conclusion: conclusions[index]));
            expected.Add(Run($"CX_{index}", "build", "COMPLETED", conclusions[index]));
        }

        for (var index = 0; index < states.Length; index++)
        {
            nodes.Add(StatusNode(id: $"SC_{index}", state: states[index]));
            expected.Add(Status($"SC_{index}", "ci/legacy", states[index]));
        }

        // 6 + 10 + 5 = 21 entries: every literal appears at least once, with an in-progress run carrying a conclusion and a completed run
        // carrying none (no undocumented pairing rule is invented).
        AssertPage(await ObserveStdoutAsync(WithNodes([.. nodes])), nodes.Count, [.. expected]);
        Assert.Contains(expected, entry => entry is { Status: "QUEUED", Conclusion: not null });
        Assert.Contains(expected, entry => entry is { Status: "COMPLETED", Conclusion: null });
    }

    [Fact]
    public async Task A_zero_total_with_an_empty_node_array_is_an_admitted_empty_page_not_a_null_rollup()
    {
        var probe = await ObserveStdoutAsync(WithRollup(Contexts("0", "false", "[]")));

        AssertPage(probe, 0);
        Assert.NotNull(probe!.Page);
        Assert.Empty(probe.Page.Contexts);
    }

    [Fact]
    public async Task A_null_rollup_is_a_returned_ref_with_no_page_and_a_null_ref_is_no_ref_and_no_page()
    {
        var withRef = await ObserveStdoutAsync(WithRollup("null"));
        var noRef = await ObserveStdoutAsync(Answer(reference: "null"));

        Assert.Equal(new GitHubCheckContextsRef(Branch, Sha), withRef!.Ref);
        Assert.Null(withRef.Page);
        Assert.Equal((NodeId, Owner, Repository), (noRef!.NodeId, noRef.Owner, noRef.Repository));
        Assert.Null(noRef.Ref);
        Assert.Null(noRef.Page);
    }

    [Fact]
    public async Task Exactly_fifty_entries_are_admitted_and_more_than_fifty_report_the_count_with_the_first_fifty_validated()
    {
        var fifty = Enumerable.Range(0, 50).Select(index => RunNode(id: $"CR_{index}")).ToArray();

        var exact = await ObserveStdoutAsync(WithRollup(Contexts("50", "false", Nodes(fifty))));
        var more = await ObserveStdoutAsync(WithRollup(Contexts("51", "true", Nodes(fifty))));
        var huge = await ObserveStdoutAsync(WithRollup(Contexts(int.MaxValue.ToString(), "true", Nodes(fifty))));

        Assert.Equal(50, exact!.Page!.Contexts.Count);
        Assert.Equal(50, exact.Page.TotalCount);
        Assert.Equal(50, more!.Page!.Contexts.Count);
        Assert.Equal(51, more.Page.TotalCount);
        Assert.Equal(int.MaxValue, huge!.Page!.TotalCount);
        Assert.Equal(Enumerable.Range(0, 50).Select(index => $"CR_{index}"), more.Page.Contexts.Select(context => context.Id));
    }

    [Fact]
    public async Task A_bounded_page_is_validated_whole_even_when_the_count_exceeds_the_bound_so_a_bad_entry_still_refuses_it()
    {
        var page = Enumerable.Range(0, 50).Select(index => RunNode(id: $"CR_{index}")).ToArray();
        page[49] = RunNode(id: "CR_49", conclusion: "success");

        Assert.Null(await ObserveStdoutAsync(WithRollup(Contexts("51", "true", Nodes(page)))));
        page[49] = RunNode(id: "CR_0");
        Assert.Null(await ObserveStdoutAsync(WithRollup(Contexts("51", "true", Nodes(page)))));
    }

    // ---- Counts and page flags. ----

    [Theory]
    [InlineData("3", "false", 2)]
    [InlineData("3", "false", 4)]
    [InlineData("0", "false", 1)]
    [InlineData("1", "false", 0)]
    [InlineData("50", "false", 49)]
    [InlineData("51", "true", 49)]
    [InlineData("51", "true", 51)]
    [InlineData("100", "true", 51)]
    [InlineData("50", "true", 50)]
    [InlineData("3", "true", 3)]
    [InlineData("0", "true", 0)]
    [InlineData("51", "false", 50)]
    [InlineData("1000", "false", 50)]
    public async Task An_inconsistent_total_node_count_or_page_flag_refuses_the_whole_answer(string total, string hasNext, int nodeCount)
    {
        var nodes = Enumerable.Range(0, nodeCount).Select(index => RunNode(id: $"CR_{index}")).ToArray();

        Assert.Null(await ObserveStdoutAsync(WithRollup(Contexts(total, hasNext, Nodes(nodes)))));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-0")]
    [InlineData("1.0")]
    [InlineData("1.5")]
    [InlineData("1e1")]
    [InlineData("1E0")]
    [InlineData("2147483648")]
    [InlineData("9223372036854775807")]
    [InlineData("01")]
    [InlineData("+1")]
    [InlineData("\"1\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task A_total_count_that_is_not_a_plain_non_negative_int32_literal_refuses_the_whole_answer(string total)
    {
        Assert.Null(await ObserveStdoutAsync(WithRollup(Contexts(total, "false", "[]"))));
        Assert.Null(await ObserveStdoutAsync(WithRollup(Contexts(total, "true", "[]"))));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("\"false\"")]
    [InlineData("\"true\"")]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task A_page_flag_that_is_not_a_json_boolean_refuses_the_whole_answer(string flag)
    {
        Assert.Null(await ObserveStdoutAsync(WithRollup(Contexts("0", flag, "[]"))));
    }

    // ---- Connection shape, mutated programmatically at every property and object of the literal answer. ----

    private sealed record Property(string Key, int KeyStart, int ValueStart, int ValueEnd);

    private static List<Property> Properties(string literal)
    {
        var properties = new List<Property>();
        foreach (Match match in PropertyKey().Matches(literal))
        {
            var valueStart = match.Index + match.Length;
            properties.Add(new Property(match.Groups[1].Value, match.Index, valueStart, ValueEnd(literal, valueStart)));
        }

        return properties;
    }

    [GeneratedRegex("\"([A-Za-z_]+)\":")]
    private static partial Regex PropertyKey();

    private static int ValueEnd(string text, int start)
    {
        if (text[start] == '"')
        {
            return text.IndexOf('"', start + 1) + 1;
        }

        if (text[start] is '{' or '[')
        {
            var depth = 0;
            var inString = false;
            for (var index = start; index < text.Length; index++)
            {
                var c = text[index];
                if (c == '"')
                {
                    inString = !inString;
                }
                else if (!inString && c is '{' or '[')
                {
                    depth++;
                }
                else if (!inString && c is '}' or ']')
                {
                    if (--depth == 0)
                    {
                        return index + 1;
                    }
                }
            }
        }

        var end = start;
        while (end < text.Length && text[end] is not (',' or '}' or ']'))
        {
            end++;
        }

        return end;
    }

    [Fact]
    public async Task The_literal_answer_is_admitted_and_carries_every_selected_property_so_the_mutations_below_are_meaningful()
    {
        var literal = Literal();
        var keys = Properties(literal).Select(property => property.Key).Distinct().Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "__typename", "checkSuite", "conclusion", "context", "contexts", "data", "hasNextPage", "id", "name", "nameWithOwner",
                "nodes", "oid", "pageInfo", "prefix", "ref", "repository", "state", "status", "statusCheckRollup", "target", "totalCount",
            ],
            keys);
        Assert.Equal(35, Properties(literal).Count);
        Assert.NotNull(await ObserveStdoutAsync(literal));
    }

    [Fact]
    public async Task An_escaped_property_name_at_every_position_of_every_level_is_refused_whole_while_the_literal_answer_is_admitted()
    {
        var literal = Literal();
        Assert.NotNull(await ObserveStdoutAsync(literal));
        var failures = new List<string>();

        foreach (var property in Properties(literal))
        {
            var escaped = literal[..(property.KeyStart + 1)] + U(property.Key[0]) + literal[(property.KeyStart + 2)..];

            Assert.NotEqual(literal, escaped);
            if (await ObserveStdoutAsync(escaped) is not null)
            {
                failures.Add($"{property.Key}@{property.KeyStart}");
            }

            if (await ObserveStdoutAsync(escaped.Replace("\"data\":{", "\"data\": {", StringComparison.Ordinal)) is not null)
            {
                failures.Add($"{property.Key}@{property.KeyStart} (spaced)");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task A_duplicate_property_at_every_position_of_every_level_refuses_the_whole_answer()
    {
        var literal = Literal();
        var failures = new List<string>();

        foreach (var property in Properties(literal))
        {
            var pair = literal[property.KeyStart..property.ValueEnd];
            var duplicated = literal[..property.ValueEnd] + "," + pair + literal[property.ValueEnd..];

            if (await ObserveStdoutAsync(duplicated) is not null)
            {
                failures.Add($"{property.Key}@{property.KeyStart}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task A_missing_member_at_every_position_of_every_level_refuses_the_whole_answer()
    {
        var literal = Literal();
        var failures = new List<string>();

        foreach (var property in Properties(literal))
        {
            var hasFollower = literal[property.ValueEnd] == ',';
            var removed = hasFollower
                ? literal[..property.KeyStart] + literal[(property.ValueEnd + 1)..]
                : literal[..(property.KeyStart - 1)] + literal[property.ValueEnd..];

            if (await ObserveStdoutAsync(removed) is not null)
            {
                failures.Add($"{property.Key}@{property.KeyStart}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task An_extra_member_in_every_object_of_every_level_refuses_the_whole_answer()
    {
        var literal = Literal();
        var failures = new List<string>();

        for (var index = 0; index < literal.Length; index++)
        {
            if (literal[index] != '}')
            {
                continue;
            }

            foreach (var extra in new[] { "\"zz\":1", "\"zz\":null", "\"url\":\"https://evil.example\"" })
            {
                if (await ObserveStdoutAsync(literal[..index] + "," + extra + literal[index..]) is not null)
                {
                    failures.Add($"{extra}@{index}");
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task A_value_of_the_wrong_json_type_or_an_explicit_null_for_a_required_value_refuses_the_whole_answer()
    {
        var literal = Literal();
        var failures = new List<string>();
        var nullable = new HashSet<string> { "conclusion", "ref", "statusCheckRollup" };

        foreach (var property in Properties(literal))
        {
            foreach (var replacement in new[] { "1", "true", "[]", "{}", "null" })
            {
                var original = literal[property.ValueStart..property.ValueEnd];
                if (original == replacement || (replacement == "null" && nullable.Contains(property.Key)))
                {
                    continue;
                }

                var mutated = literal[..property.ValueStart] + replacement + literal[property.ValueEnd..];
                if (await ObserveStdoutAsync(mutated) is not null)
                {
                    failures.Add($"{property.Key}@{property.KeyStart} -> {replacement}");
                }
            }
        }

        // The three documented nullable values are the only ones a null may replace while the answer stays admitted.
        Assert.Empty(failures);
        Assert.NotNull(await ObserveStdoutAsync(WithRollup("null")));
        Assert.NotNull(await ObserveStdoutAsync(Answer(reference: "null")));
        Assert.NotNull(await ObserveStdoutAsync(WithNodes(RunNode(conclusion: null))));
    }

    [Fact]
    public async Task A_string_literal_substituted_for_a_string_value_is_admitted_only_where_the_value_itself_is_admissible()
    {
        // "x" is a valid label and a valid opaque identifier, so it is admitted for those properties and refused for the closed ones.
        var literal = Literal();
        var admitted = new List<string>();

        foreach (var property in Properties(literal).Where(property => literal[property.ValueStart] == '"'))
        {
            var mutated = literal[..property.ValueStart] + "\"x\"" + literal[property.ValueEnd..];
            if (await ObserveStdoutAsync(mutated) is not null)
            {
                admitted.Add(property.Key);
            }
        }

        // Admitted: the repository id, three entry ids and two suite ids (all "id"), the two check run names and the one status label.
        // Refused everywhere else: repository and ref names, nameWithOwner, prefix, oid, every typename, status, conclusion and state.
        Assert.Equal(
            ["context", "id", "id", "id", "id", "id", "id", "name", "name"],
            admitted.OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("\"nodes\":null")]
    [InlineData("\"nodes\":{}")]
    [InlineData("\"nodes\":\"x\"")]
    [InlineData("\"nodes\":[null]")]
    [InlineData("\"nodes\":[1]")]
    [InlineData("\"nodes\":[[]]")]
    [InlineData("\"nodes\":[{}]")]
    [InlineData("\"nodes\":[\"x\"]")]
    public async Task A_null_or_malformed_node_collection_or_a_partial_entry_refuses_the_whole_answer(string nodes)
    {
        var answer = WithRollup("{\"contexts\":{\"totalCount\":1,\"pageInfo\":{\"hasNextPage\":false}," + nodes + "}}");

        Assert.Null(await ObserveStdoutAsync(answer));
    }

    [Theory]
    [InlineData("{\"contexts\":null}")]
    [InlineData("{\"contexts\":[]}")]
    [InlineData("{}")]
    [InlineData("{\"contexts\":{}}")]
    [InlineData("{\"contexts\":{\"totalCount\":0,\"nodes\":[]}}")]
    [InlineData("{\"contexts\":{\"pageInfo\":{\"hasNextPage\":false},\"nodes\":[]}}")]
    [InlineData("{\"contexts\":{\"totalCount\":0,\"pageInfo\":{\"hasNextPage\":false}}}")]
    [InlineData("{\"contexts\":{\"totalCount\":0,\"pageInfo\":{},\"nodes\":[]}}")]
    [InlineData("{\"contexts\":{\"totalCount\":0,\"pageInfo\":null,\"nodes\":[]}}")]
    [InlineData("{\"contexts\":{\"totalCount\":0,\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":\"x\"},\"nodes\":[]}}")]
    [InlineData("{\"contexts\":{\"totalCount\":0,\"pageInfo\":{\"endCursor\":\"x\"},\"nodes\":[]}}")]
    [InlineData("{\"contexts\":{\"totalCount\":0,\"pageInfo\":{\"hasNextPage\":false},\"nodes\":[],\"edges\":[]}}")]
    [InlineData("{\"contexts\":{\"totalCount\":0,\"pageInfo\":{\"hasNextPage\":false},\"nodes\":[]},\"state\":\"SUCCESS\"}")]
    [InlineData("{\"state\":\"SUCCESS\"}")]
    [InlineData("{\"contexts\":{\"totalCount\":0,\"pageInfo\":{\"hasNextPage\":false},\"nodes\":[]},\"contexts\":{\"totalCount\":0,\"pageInfo\":{\"hasNextPage\":false},\"nodes\":[]}}")]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    [InlineData("0")]
    [InlineData("false")]
    public async Task An_incomplete_extra_or_ambiguous_connection_is_never_admitted(string rollup)
    {
        Assert.Null(await ObserveStdoutAsync(WithRollup(rollup)));
    }

    [Theory]
    [InlineData("{\"__typename\":\"CheckRun\",\"id\":\"CR_1\",\"name\":\"build\",\"status\":\"COMPLETED\",\"conclusion\":null,\"checkSuite\":null}")]
    [InlineData("{\"__typename\":\"CheckRun\",\"id\":\"CR_1\",\"name\":\"build\",\"status\":\"COMPLETED\",\"conclusion\":null,\"checkSuite\":{}}")]
    [InlineData("{\"__typename\":\"CheckRun\",\"id\":\"CR_1\",\"name\":\"build\",\"status\":\"COMPLETED\",\"conclusion\":null,\"checkSuite\":{\"id\":\"CS_1\",\"app\":null}}")]
    [InlineData("{\"__typename\":\"CheckRun\",\"id\":\"CR_1\",\"name\":\"build\",\"status\":\"COMPLETED\",\"conclusion\":null}")]
    [InlineData("{\"__typename\":\"CheckRun\",\"id\":\"CR_1\",\"name\":\"build\",\"status\":\"COMPLETED\",\"checkSuite\":{\"id\":\"CS_1\"}}")]
    [InlineData("{\"__typename\":\"CheckRun\",\"id\":\"CR_1\",\"context\":\"build\",\"status\":\"COMPLETED\",\"conclusion\":null,\"checkSuite\":{\"id\":\"CS_1\"}}")]
    [InlineData("{\"__typename\":\"CheckRun\",\"id\":\"CR_1\",\"name\":\"build\",\"state\":\"SUCCESS\",\"conclusion\":null,\"checkSuite\":{\"id\":\"CS_1\"}}")]
    [InlineData("{\"__typename\":\"StatusContext\",\"id\":\"SC_1\",\"context\":\"x\",\"state\":\"SUCCESS\",\"conclusion\":null}")]
    [InlineData("{\"__typename\":\"StatusContext\",\"id\":\"SC_1\",\"context\":\"x\",\"state\":\"SUCCESS\",\"checkSuite\":{\"id\":\"CS_1\"}}")]
    [InlineData("{\"__typename\":\"StatusContext\",\"id\":\"SC_1\",\"name\":\"x\",\"state\":\"SUCCESS\"}")]
    [InlineData("{\"__typename\":\"StatusContext\",\"id\":\"SC_1\",\"context\":\"x\",\"status\":\"SUCCESS\"}")]
    [InlineData("{\"__typename\":\"StatusContext\",\"id\":\"SC_1\",\"context\":\"x\"}")]
    [InlineData("{\"__typename\":\"Status\",\"id\":\"SC_1\",\"context\":\"x\",\"state\":\"SUCCESS\"}")]
    [InlineData("{\"__typename\":\"checkrun\",\"id\":\"CR_1\",\"name\":\"build\",\"status\":\"COMPLETED\",\"conclusion\":null,\"checkSuite\":{\"id\":\"CS_1\"}}")]
    [InlineData("{\"__typename\":\"CheckSuite\",\"id\":\"CR_1\",\"name\":\"build\",\"status\":\"COMPLETED\",\"conclusion\":null,\"checkSuite\":{\"id\":\"CS_1\"}}")]
    [InlineData("{\"__typename\":null,\"id\":\"CR_1\"}")]
    [InlineData("{\"id\":\"CR_1\",\"name\":\"build\",\"status\":\"COMPLETED\",\"conclusion\":null,\"checkSuite\":{\"id\":\"CS_1\"}}")]
    public async Task A_node_that_is_not_exactly_one_known_typename_with_exactly_its_selected_fields_refuses_the_whole_answer(string node)
    {
        Assert.Null(await ObserveStdoutAsync(WithNodes(node)));
    }

    [Fact]
    public async Task A_duplicate_entry_identifier_refuses_the_whole_answer_ordinally_and_never_deduplicates()
    {
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(id: "CR_1", name: "a"), RunNode(id: "CR_1", name: "b"))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(id: "X_1"), StatusNode(id: "X_1"))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(StatusNode(id: "SC_1", context: "a"), StatusNode(id: "SC_1", context: "a"))));
        Assert.NotNull(await ObserveStdoutAsync(WithNodes(RunNode(id: "CR_a"), RunNode(id: "CR_A"))));
        Assert.NotNull(await ObserveStdoutAsync(WithNodes(
            RunNode(id: "CR_1", name: "same", suite: "CS_1"), RunNode(id: "CR_2", name: "same", suite: "CS_1"))));
    }

    // ---- Literal enumerations and identifiers. ----

    [Theory]
    [InlineData("completed")]
    [InlineData("SUCCESS")]
    [InlineData("FAILURE")]
    [InlineData("STARTUP_FAILURE")]
    [InlineData("COMPLETED ")]
    [InlineData("RUNNING")]
    [InlineData("")]
    public async Task An_unsupported_check_run_status_refuses_the_whole_answer(string status)
    {
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(status: status))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(id: "CR_0"), RunNode(id: "CR_1", status: status))));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("SUCCESS ")]
    [InlineData("COMPLETED")]
    [InlineData("PENDING")]
    [InlineData("ERROR")]
    [InlineData("CANCELED")]
    [InlineData("PASSED")]
    [InlineData("")]
    public async Task An_unsupported_check_run_conclusion_refuses_the_whole_answer(string conclusion)
    {
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(conclusion: conclusion))));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("QUEUED")]
    [InlineData("COMPLETED")]
    [InlineData("NEUTRAL")]
    [InlineData("SUCCESSFUL")]
    [InlineData("")]
    public async Task An_unsupported_status_context_state_refuses_the_whole_answer(string state)
    {
        Assert.Null(await ObserveStdoutAsync(WithNodes(StatusNode(state: state))));
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("CR_kwDOabc-def/ghi+jkl=", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("a.b", false)]
    [InlineData("a:b", false)]
    public async Task An_entry_and_suite_identifier_follows_the_conservative_policy_without_decoding(string id, bool admitted)
    {
        Assert.Equal(admitted, await ObserveStdoutAsync(WithNodes(RunNode(id: id))) is not null);
        Assert.Equal(admitted, await ObserveStdoutAsync(WithNodes(RunNode(suite: id))) is not null);
        Assert.Equal(admitted, await ObserveStdoutAsync(WithNodes(StatusNode(id: id))) is not null);
    }

    [Fact]
    public async Task An_identifier_is_bounded_to_256_ascii_characters_and_a_non_ascii_or_escaped_identifier_is_refused()
    {
        Assert.NotNull(await ObserveStdoutAsync(WithNodes(RunNode(id: new string('a', 256), suite: new string('b', 256)))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(id: new string('a', 257)))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(suite: new string('b', 257)))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(id: "CR_" + C(0xE9)))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(id: "CR_" + U(0x31)))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(suite: "CS_" + U(0x31)))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(StatusNode(id: "SC_" + U(0x31)))));
    }

    [Fact]
    public async Task A_json_escape_in_any_value_other_than_a_label_refuses_the_whole_answer()
    {
        var escapedA = U(0x41);
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(status: "COMPL" + U(0x45) + "TED"))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(conclusion: "SUCC" + U(0x45) + "SS"))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(StatusNode(state: "SUCC" + U(0x45) + "SS"))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode()).Replace("\"CheckRun\"", "\"Check" + U(0x52) + "un\"", StringComparison.Ordinal)));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode()).Replace(Sha, Sha[..39] + escapedA, StringComparison.Ordinal)));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode()).Replace("octo-cat", "octo" + U(0x2D) + "cat", StringComparison.Ordinal)));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode()).Replace("refs/heads/", "refs" + Backslash('/') + "heads" + Backslash('/'), StringComparison.Ordinal)));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode()).Replace(NodeId, "R_k" + U(0x67) + "DOExample01", StringComparison.Ordinal)));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode()).Replace(Branch, Branch[..^1] + U(0x33), StringComparison.Ordinal)));
    }

    [Fact]
    public async Task An_invalid_surrogate_or_escape_in_a_value_that_is_not_a_label_is_refused_before_the_decoder_and_never_throws()
    {
        foreach (var bad in new[] { U(0xD800), U(0xDC00), U(0xD83D) + U(0x41), "x" + Backslash('q'), "x\\" + "u12" })
        {
            Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(id: "CR_" + bad))));
            Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(suite: "CS_" + bad))));
            Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(status: "COMPLETED" + bad))));
            Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(conclusion: "SUCCESS" + bad))));
            Assert.Null(await ObserveStdoutAsync(WithNodes(StatusNode(state: "SUCCESS" + bad))));
            Assert.Null(await ObserveStdoutAsync(WithNodes(StatusNode(id: "SC_" + bad))));
            Assert.Null(await ObserveStdoutAsync(Answer(id: NodeId + bad)));
            Assert.Null(await ObserveStdoutAsync(Answer(name: Repository + bad, nameWithOwner: Owner + "/" + Repository + bad)));
            Assert.Null(await ObserveStdoutAsync(WithRef(RefJson(oid: Sha[..39] + bad))));
            Assert.Null(await ObserveStdoutAsync(WithRef(RefJson(prefix: "refs/heads/" + bad))));
        }
    }

    // ---- The label text contract. ----

    private async Task<GitHubCheckContextsProbe?> ObserveLabelAsync(string jsonLabelBody, bool statusContext = false) =>
        await ObserveStdoutAsync(WithNodes(statusContext
            ? StatusNode(context: jsonLabelBody)
            : RunNode(name: jsonLabelBody)));

    [Fact]
    public async Task A_label_is_decoded_once_and_the_exact_text_is_preserved_including_quotes_backslashes_unicode_and_markup()
    {
        var cases = new (string Json, string Expected)[]
        {
            ("build", "build"),
            (Backslash('"') + "quoted" + Backslash('"'), "\"quoted\""),
            ("back" + Backslash('\\') + "slash", "back\\slash"),
            ("a" + Backslash('/') + "b", "a/b"),
            (Backslash('\\') + "u0041", "\\" + "u0041"),
            ("literal " + Backslash('\\') + Backslash('\\') + " pair", "literal " + "\\\\" + " pair"),
            ("<script>alert(1)</script>", "<script>alert(1)</script>"),
            ("[x](https://evil.example) **b** &amp;", "[x](https://evil.example) **b** &amp;"),
            (U(0x41) + U(0x42), "AB"),
            (U(0xE9), C(0xE9)),
            (U(0xD83D) + U(0xDE00), C(0x1F600)),
            (C(0x65E5) + C(0x672C), C(0x65E5) + C(0x672C)),
            (C(0x1F600), C(0x1F600)),
            (" padded ", " padded "),
            ("tab-free " + C(0xA0) + " nbsp", "tab-free " + C(0xA0) + " nbsp"),
        };

        foreach (var (json, expected) in cases)
        {
            var check = await ObserveLabelAsync(json);
            var status = await ObserveLabelAsync(json, statusContext: true);

            Assert.True(check is not null, json);
            Assert.True(status is not null, json);
            Assert.Equal(expected, Assert.Single(check!.Page!.Contexts).Name);
            Assert.Equal(expected, Assert.Single(status!.Page!.Contexts).Name);
        }
    }

    [Fact]
    public async Task A_label_whose_decoded_text_is_blank_control_format_separator_or_replacement_refuses_the_whole_answer()
    {
        var refused = new List<string>
        {
            string.Empty,
            " ",
            "   ",
            U(0x20),
            U(0xA0),
            U(0x3000),
            "a" + U(0x00) + "b",
            "a" + U(0x09) + "b",
            "a" + Backslash('n') + "b",
            "a" + Backslash('r') + "b",
            "a" + Backslash('t') + "b",
            "a" + Backslash('b') + "b",
            "a" + Backslash('f') + "b",
            "a" + U(0x1F) + "b",
            "a" + U(0x7F) + "b",
            "a" + U(0x85) + "b",
            "a" + U(0x9F) + "b",
            "a" + U(0xAD) + "b",
            "a" + U(0x200B) + "b",
            "a" + U(0x200E) + "b",
            "a" + U(0x202E) + "b",
            "a" + U(0x2066) + "b",
            "a" + U(0xFEFF) + "b",
            "a" + U(0x2028) + "b",
            "a" + U(0x2029) + "b",
            "a" + U(0xFFFD) + "b",
            "a" + C(0xFFFD) + "b",
            C(0xFFFD),
            "a" + C(0x200B) + "b",
            "a" + C(0x2028) + "b",
            "a" + C(0xE0001) + "b",
        };

        foreach (var json in refused)
        {
            Assert.True(await ObserveLabelAsync(json) is null, $"check run: {json.Length} chars: {string.Join(' ', json.Select(c => ((int)c).ToString("x4")))}");
            Assert.True(await ObserveLabelAsync(json, statusContext: true) is null, $"status context: {json.Length} chars");
        }
    }

    [Fact]
    public async Task An_invalid_or_malformed_string_escape_or_surrogate_is_refused_not_replaced_and_never_an_unhandled_parsing_fault()
    {
        var refused = new[]
        {
            "a" + U(0xD800) + "b",
            "a" + U(0xDC00) + "b",
            "a" + U(0xDC00) + U(0xD800) + "b",
            U(0xD83D),
            U(0xD83D) + "x",
            "x" + U(0xDE00),
            U(0xD83D) + U(0x41),
            "a" + Backslash('q') + "b",
            "a" + Backslash('x') + "41b",
            "a" + Backslash('a') + "b",
            "a" + Backslash('0') + "b",
            "a\\" + "u12",
            "a\\" + "u",
            "a\\" + "uZZZZb",
            "a\\" + "u12G4b",
            "trailing" + "\\",
            "a" + Backslash('U') + "0041b",
        };

        foreach (var json in refused)
        {
            Assert.True(await ObserveLabelAsync(json) is null, json);
            Assert.True(await ObserveLabelAsync(json, statusContext: true) is null, json);
        }

        // A raw control character or an unescaped quote inside the JSON string is not JSON at all.
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(name: "a\nb"))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(name: "a\tb"))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(name: "a\"b"))));
    }

    [Fact]
    public async Task A_label_is_bounded_to_256_utf8_bytes_after_decoding_whatever_the_escaping()
    {
        Assert.NotNull(await ObserveLabelAsync(new string('a', 256)));
        Assert.Null(await ObserveLabelAsync(new string('a', 257)));
        Assert.NotNull(await ObserveLabelAsync(string.Concat(Enumerable.Repeat(U(0xE9), 128))));
        Assert.Null(await ObserveLabelAsync(string.Concat(Enumerable.Repeat(U(0xE9), 129))));
        Assert.NotNull(await ObserveLabelAsync(string.Concat(Enumerable.Repeat(U(0x61), 256))));
        Assert.Null(await ObserveLabelAsync(string.Concat(Enumerable.Repeat(U(0x61), 257))));
        Assert.NotNull(await ObserveLabelAsync(string.Concat(Enumerable.Repeat(C(0x65E5), 85)) + "a"));
        Assert.Null(await ObserveLabelAsync(string.Concat(Enumerable.Repeat(C(0x65E5), 86))));
        Assert.NotNull(await ObserveLabelAsync(string.Concat(Enumerable.Repeat(U(0xD83D) + U(0xDE00), 64))));
        Assert.Null(await ObserveLabelAsync(string.Concat(Enumerable.Repeat(U(0xD83D) + U(0xDE00), 65))));
        Assert.NotNull(await ObserveLabelAsync(string.Concat(Enumerable.Repeat(Backslash('"'), 256))));
        Assert.Null(await ObserveLabelAsync(string.Concat(Enumerable.Repeat(Backslash('"'), 257))));
    }

    [Fact]
    public async Task One_malformed_label_among_valid_siblings_refuses_the_whole_answer_and_never_omits_the_bad_entry()
    {
        var good = new[] { RunNode(id: "CR_1", name: "ok-1"), StatusNode(id: "SC_1", context: "ok-2"), RunNode(id: "CR_2", name: "ok-3") };

        foreach (var badPosition in new[] { 0, 1, 2, 3 })
        {
            var nodes = good.ToList();
            nodes.Insert(badPosition, RunNode(id: "CR_bad", name: "bad" + U(0x00)));

            Assert.Null(await ObserveStdoutAsync(WithNodes([.. nodes])));
        }

        Assert.NotNull(await ObserveStdoutAsync(WithNodes(good)));
    }

    [Fact]
    public async Task A_backslash_in_a_label_is_not_a_blanket_refusal_and_does_not_weaken_the_refusal_elsewhere()
    {
        Assert.NotNull(await ObserveLabelAsync("back" + Backslash('\\') + "slash"));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(id: "CR_1", name: "ok")).Replace("\"data\"", "\"" + U(0x64) + "ata\"", StringComparison.Ordinal)));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(id: "CR_1", name: "ok" + Backslash('\\'))).Replace("COMPLETED", "COMPLETE" + U(0x44), StringComparison.Ordinal)));
    }

    // ---- Redaction after decoding: a label is safe post-redaction text, never the raw escaped provider text. ----
    // The five supported secret-shape classes of the unchanged redactor are built here from fragments, so no complete token-shaped value
    // exists in this source and no assertion message ever prints one. Every shape is fictitious.

    private const string Redacted = "[REDACTED]";

    public static TheoryData<string> SecretShapeClasses => new() { "Bearer", "GitHubFineGrained", "Slack", "GitHubClassic", "AwsKeyId" };

    private static string SecretShape(string shapeClass) => shapeClass switch
    {
        "Bearer" => "Bearer " + "A1b2C3d4E5f6",
        "GitHubFineGrained" => "github" + "_pat_" + new string('b', 30),
        "Slack" => "xox" + "b-" + "1234567890ab",
        "GitHubClassic" => "gh" + "p_" + new string('c', 36),
        "AwsKeyId" => "AK" + "IA" + "ABCDEFGHIJKLMNOP",
        _ => throw new ArgumentOutOfRangeException(nameof(shapeClass)),
    };

    /// <summary>The label body as the JSON text a provider could send: the shape literal, its first character escaped, or every
    /// character escaped.</summary>
    private static string[] ShapeSpellings(string shape) =>
    [
        shape,
        U(shape[0]) + shape[1..],
        string.Concat(shape.Select(character => U(character))),
    ];

    private static void AssertLabel(string? actual, string expected, string context) =>
        Assert.True(actual == expected, $"{context}: the admitted label is not the expected safe text");

    [Theory]
    [MemberData(nameof(SecretShapeClasses))]
    public async Task A_supported_secret_shape_is_redacted_in_a_decoded_label_whether_literal_or_escaped_for_both_label_kinds(string shapeClass)
    {
        var shape = SecretShape(shapeClass);
        foreach (var spelling in ShapeSpellings(shape))
        {
            var run = await ObserveLabelAsync("build " + spelling + " done");
            var status = await ObserveLabelAsync("ci " + spelling + " ok", statusContext: true);

            Assert.True(run is not null && status is not null, $"{shapeClass}: the answer was refused");
            AssertLabel(Assert.Single(run!.Page!.Contexts).Name, "build " + Redacted + " done", shapeClass + " check run");
            AssertLabel(Assert.Single(status!.Page!.Contexts).Name, "ci " + Redacted + " ok", shapeClass + " status context");
        }
    }

    [Theory]
    [MemberData(nameof(SecretShapeClasses))]
    public async Task A_label_that_is_only_a_supported_secret_shape_becomes_only_the_redaction_marker(string shapeClass)
    {
        foreach (var spelling in ShapeSpellings(SecretShape(shapeClass)))
        {
            AssertLabel(Assert.Single((await ObserveLabelAsync(spelling))!.Page!.Contexts).Name, Redacted, shapeClass);
            AssertLabel(Assert.Single((await ObserveLabelAsync(spelling, statusContext: true))!.Page!.Contexts).Name, Redacted, shapeClass);
        }
    }

    [Fact]
    public async Task The_redacted_label_is_what_the_whole_page_carries_and_siblings_are_unchanged()
    {
        var shape = SecretShape("GitHubClassic");
        var probe = await ObserveStdoutAsync(WithNodes(
            RunNode(id: "CR_1", name: "plain"),
            RunNode(id: "CR_2", name: U(shape[0]) + shape[1..]),
            StatusNode(id: "SC_1", context: "also plain"),
            StatusNode(id: "SC_2", context: shape)));

        Assert.NotNull(probe);
        Assert.True(
            probe.Page!.Contexts.Select(context => context.Name).SequenceEqual(["plain", Redacted, "also plain", Redacted]),
            "the page labels are not the expected safe texts");
        Assert.Equal(["CR_1", "CR_2", "SC_1", "SC_2"], probe.Page.Contexts.Select(context => context.Id));
        Assert.True(!probe.Page.Contexts.Any(context => context.Name.Contains(shape, StringComparison.Ordinal)), "a label carries a supported secret shape");
    }

    [Fact]
    public async Task Text_that_only_resembles_a_supported_shape_and_legitimate_escaped_text_are_preserved_exactly()
    {
        var preserved = new[]
        {
            "gh" + "p_" + "short",
            "Bearer " + "short",
            "AK" + "IA" + "TOOSHORT",
            "xox" + "b-" + "short",
            "github" + "_pat_" + "short",
            "say \"hi\" and back\\slash <b>x</b>",
            "ol" + C(0xE1) + " " + C(0x65E5) + " " + C(0x1F600),
        };
        foreach (var label in preserved)
        {
            var escaped = string.Concat(label.Select(character => character is '"' or '\\' ? Backslash(character).ToString() : character.ToString()));
            var probe = await ObserveLabelAsync(escaped);

            AssertLabel(Assert.Single(probe!.Page!.Contexts).Name, label, "preserved text");
        }

        // The first character of an ordinary word may be escaped without any effect on the text.
        AssertLabel(Assert.Single((await ObserveLabelAsync(U('b') + "uild"))!.Page!.Contexts).Name, "build", "escaped ordinary word");
    }

    [Fact]
    public async Task A_label_is_decoded_exactly_once_before_redaction_so_a_double_escaped_shape_stays_literal_text()
    {
        var shape = SecretShape("GitHubClassic");
        // An escaped backslash followed by the text of an escape: one decode yields a backslash, "u" and four digits, which is not a
        // supported shape, and a second decode (which must not happen) would restore the shape and redact it.
        var doubleEscaped = Backslash('\\') + "u" + ((int)shape[0]).ToString("x4") + shape[1..];

        var name = Assert.Single((await ObserveLabelAsync(doubleEscaped))!.Page!.Contexts).Name;

        AssertLabel(name, "\\" + "u" + ((int)shape[0]).ToString("x4") + shape[1..], "single decode");
    }

    [Fact]
    public async Task The_decoded_label_is_validated_before_redaction_and_the_redacted_label_is_validated_again()
    {
        var shape = SecretShape("GitHubClassic");
        // 257 decoded bytes containing a shape: redaction would shorten it to a legal length, but the decoded text is already over
        // the bound, so the whole answer is refused rather than rescued.
        var tooLong = new string('a', 257 - shape.Length) + shape;
        Assert.Equal(257, tooLong.Length);
        Assert.Null(await ObserveLabelAsync(tooLong));
        Assert.Null(await ObserveLabelAsync(U(shape[0]) + shape[1..] + new string('a', 257 - shape.Length), statusContext: true));
        // Exactly 256 decoded bytes containing a shape is admitted and the safe text is shorter.
        var atTheBound = new string('a', 256 - shape.Length) + shape;
        AssertLabel(Assert.Single((await ObserveLabelAsync(atTheBound))!.Page!.Contexts).Name, new string('a', 256 - shape.Length) + Redacted, "bound");
        // A control character is still refused wherever the shape sits.
        Assert.Null(await ObserveLabelAsync(shape + U(0x0A) + "x"));
        Assert.Null(await ObserveLabelAsync(U(0x200B) + shape));
    }

    [Fact]
    public async Task Redaction_changes_only_labels_and_never_the_closed_literals_or_identifiers()
    {
        var shape = SecretShape("GitHubClassic");
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(status: "COMPLETED" + shape))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(RunNode(id: "CR_" + U(shape[0]) + shape[1..]))));
        Assert.Null(await ObserveStdoutAsync(WithNodes(StatusNode(state: shape))));
    }

    // ---- Envelope, ref, target and identity (the unchanged ADR-0044 shape rules, restated for this operation). ----

    [Theory]
    [InlineData("v1", "refs/tags/", "Commit")]
    [InlineData("main", "refs/heads/", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/4", "refs/heads/", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/tags/", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "", "Commit")]
    [InlineData("refs/heads/devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads/", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3 ", "refs/heads/", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads/", "Tag")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads/", "Tree")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads/", "commit")]
    public async Task A_ref_that_is_not_exactly_the_recorded_branch_head_commit_is_refused_even_with_a_valid_collection(
        string name, string prefix, string typeName)
    {
        Assert.Null(await ObserveStdoutAsync(WithRef(RefJson(name, prefix, typeName, rollup: Rollup(RunNode())))));
    }

    [Theory]
    [InlineData("0123456789ABCDEF0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456")]
    [InlineData("0123456789abcdef0123456789abcdef012345678")]
    [InlineData("0123456789abcdef0123456789abcdef0123456g")]
    [InlineData(" 0123456789abcdef0123456789abcdef0123456")]
    [InlineData("")]
    public async Task An_object_id_that_is_not_forty_lowercase_hexadecimal_characters_is_refused(string oid)
    {
        Assert.Null(await ObserveStdoutAsync(WithRef(RefJson(oid: oid, rollup: Rollup(RunNode())))));
    }

    [Theory]
    [InlineData("""{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567"}""")]
    [InlineData("""{"__typename":"Commit","statusCheckRollup":null}""")]
    [InlineData("""{"oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}""")]
    [InlineData("""{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null,"message":"x"}""")]
    [InlineData("""{"__typename":"Tag"}""")]
    [InlineData("""{"__typename":"Commit","oid":null,"statusCheckRollup":null}""")]
    [InlineData("""{}""")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("0")]
    public async Task A_target_that_is_not_exactly_a_commit_with_its_oid_and_its_rollup_member_refuses_the_whole_answer(string target)
    {
        Assert.Null(await ObserveStdoutAsync(WithTarget(target)));
    }

    [Fact]
    public async Task A_returned_ref_with_a_missing_rollup_member_is_never_the_same_as_a_null_rollup()
    {
        Assert.Null(await ObserveStdoutAsync(WithRef(
            "{\"name\":\"" + Branch + "\",\"prefix\":\"refs/heads/\",\"target\":{\"__typename\":\"Commit\",\"oid\":\"" + Sha + "\"}}")));
        Assert.NotNull(await ObserveStdoutAsync(WithRollup("null")));
    }

    [Fact]
    public async Task Names_that_differ_from_the_stored_scope_only_in_ascii_case_are_accepted_and_reported_as_returned()
    {
        var probe = await ObserveStdoutAsync(Answer(name: "Hello.World", nameWithOwner: "OCTO-cat/Hello.World"));

        Assert.Equal(("OCTO-cat", "Hello.World"), (probe?.Owner, probe?.Repository));
    }

    [Theory]
    [InlineData("someone-else", "hello.world")]
    [InlineData("octo-cat", "renamed.repo")]
    [InlineData("octo-cat", "hello.world.git")]
    [InlineData("octo-cat2", "hello.world")]
    public async Task A_foreign_or_renamed_repository_is_never_adopted_even_with_a_valid_ref_and_collection(string owner, string repository)
    {
        Assert.Null(await ObserveStdoutAsync(Answer(
            name: repository, nameWithOwner: $"{owner}/{repository}", reference: RefJson(rollup: Rollup(RunNode())))));
        Assert.Null(await ObserveStdoutAsync(Answer(name: repository, nameWithOwner: $"{owner}/{repository}", reference: "null")));
    }

    [Theory]
    [InlineData("R_kgDOabc-def/ghi+jkl=", true)]
    [InlineData("R", true)]
    [InlineData("", false)]
    [InlineData("R kg", false)]
    [InlineData("R.kg", false)]
    public async Task The_opaque_repository_node_id_follows_the_conservative_policy(string id, bool admitted)
    {
        Assert.Equal(admitted, (await ObserveStdoutAsync(Answer(id: id))) is not null);
    }

    [Theory]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}},"errors":[{"message":"x"}]}""")]
    [InlineData("""{"errors":[{"message":"x"}],"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}},"extensions":{"cost":1}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null},"viewer":{"login":"x"}}}""")]
    [InlineData("""{"data":{"repository":null}}""")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}}} {"data":{}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}}} trailing""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}},}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}}} // comment""")]
    [InlineData("octo-cat/hello.world\nR_x")]
    public async Task Every_other_envelope_is_refused_whole(string stdout)
    {
        Assert.Null(await ObserveStdoutAsync(stdout));
    }

    [Fact]
    public async Task Whitespace_property_order_and_a_crlf_are_admitted_beside_the_escape_refusal()
    {
        var reordered = "{ \"data\" : { \"repository\" : { \"ref\" : { \"target\" : { \"statusCheckRollup\" : { \"contexts\" : { \"nodes\" : [ "
            + "{ \"checkSuite\" : { \"id\" : \"CS_1\" }, \"conclusion\" : null, \"status\" : \"QUEUED\", \"name\" : \"b\", \"id\" : \"CR_1\", "
            + "\"__typename\" : \"CheckRun\" } ], \"pageInfo\" : { \"hasNextPage\" : false }, \"totalCount\" : 1 } }, \"oid\" : \"" + Sha
            + "\", \"__typename\" : \"Commit\" }, \"prefix\" : \"refs/heads/\", \"name\" : \"" + Branch + "\" },"
            + " \"nameWithOwner\" : \"octo-cat/hello.world\", \"name\" : \"hello.world\", \"id\" : \"R_kgDOExample01\" } } }\r\n";

        var probe = await ObserveStdoutAsync(reordered);

        AssertPage(probe, 1, Run("CR_1", "b", "QUEUED", null, "CS_1"));
    }

    [Fact]
    public async Task A_json_nesting_deeper_than_twelve_levels_is_refused_and_the_valid_answer_is_well_within_the_limit()
    {
        var deep = new string('[', 13) + new string(']', 13);
        var nested = WithRollup(Contexts("0", "false", "[]")).Replace("\"data\":{", "\"data\":{\"x\":" + deep + ",", StringComparison.Ordinal);

        Assert.Null(await ObserveStdoutAsync(nested));
        Assert.Null(await ObserveStdoutAsync(deep));
        Assert.NotNull(await ObserveStdoutAsync(Literal()));
    }

    // ---- The process outcome. ----

    [Fact]
    public async Task A_zero_exit_alone_is_never_an_observation()
    {
        Assert.Null(await ObserveStdoutAsync(string.Empty));
        Assert.Null(await ObserveStdoutAsync("ok"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task A_nonzero_exit_is_unknown_even_with_a_valid_looking_body(int exitCode)
    {
        Assert.Null(await ObserveStdoutAsync(Literal(), exitCode));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_truncated_stream_is_refused_even_with_a_valid_looking_body(bool stdoutTruncated, bool stderrTruncated)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(
            Result(Literal(), stdoutTruncated: stdoutTruncated, stderrTruncated: stderrTruncated)));

        Assert.Null(await Create(process).ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Fact]
    public async Task Stderr_content_never_changes_the_outcome_and_is_never_returned()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(
            Result(Literal(), stderr: "HTTP 404 Not Found ghp_sentinel")));

        var probe = await Create(process).ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None);

        Assert.NotNull(probe);
        Assert.DoesNotContain("sentinel", probe.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timed_out_process_is_refused()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(Literal(), outcome: ProcessExecutionOutcome.TimedOut)));

        Assert.Null(await Create(process).ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Fact]
    public async Task An_os_process_start_failure_is_refused_and_exposes_no_exception_detail()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromException<ProcessExecutionResult>(
            new System.ComponentModel.Win32Exception(5, @"cannot start C:\secret\path ghp_sentinel")));

        Assert.Null(await Create(process).ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Fact]
    public async Task A_vetted_executable_that_vanished_is_refused()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromException<ProcessExecutionResult>(
            new ArgumentException(@"does not exist C:\secret\path", nameof(ProcessExecutionRequest.ExecutablePath))));

        Assert.Null(await Create(process).ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));
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

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => Create(process).ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));

        Assert.Same(fault, thrown);
    }

    [Theory]
    [MemberData(nameof(ProgrammingFaults))]
    public async Task The_slot_is_released_through_finally_when_a_fault_propagates(Exception fault)
    {
        var calls = 0;
        var process = new RecordingProcessAdapter((_, _) =>
            ++calls == 1 ? Task.FromException<ProcessExecutionResult>(fault) : Task.FromResult(Result(Answer())));
        var adapter = Create(process);

        await Assert.ThrowsAnyAsync<Exception>(() => adapter.ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));

        Assert.NotNull(await adapter.ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Fact]
    public async Task A_scratch_directory_fault_propagates_and_releases_the_slot()
    {
        var calls = 0;
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(Answer())));
        var adapter = new GitHubCheckContextsObservationAdapter(
            process, () => ++calls == 1 ? throw new IOException("scratch") : _scratch, _ => null);

        await Assert.ThrowsAsync<IOException>(() => adapter.ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));

        Assert.NotNull(await adapter.ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_is_raised_not_reported_as_a_status_and_releases_the_slot()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var process = new RecordingProcessAdapter((_, _) => ++calls == 1
            ? Task.FromResult(Result(string.Empty, outcome: ProcessExecutionOutcome.Cancelled))
            : Task.FromResult(Result(Answer())));
        var adapter = Create(process);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.ObserveAsync(GhPath, Owner, Repository, Branch, cancellation.Token));

        Assert.NotNull(await adapter.ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Fact]
    public async Task A_thrown_cancellation_propagates_and_releases_the_slot()
    {
        var calls = 0;
        var process = new RecordingProcessAdapter((_, _) =>
            ++calls == 1 ? throw new OperationCanceledException() : Task.FromResult(Result(Answer())));
        var adapter = Create(process);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));

        Assert.NotNull(await adapter.ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Fact]
    public async Task A_concurrent_request_is_refused_immediately_with_no_queue_retry_or_extra_spawn_and_the_slot_is_released_after_the_first()
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

            return Task.FromResult(Result(Answer()));
        });
        var adapter = Create(process);

        var first = adapter.ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None);
        await started.Task;
        var concurrent = await adapter.ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(concurrent);
        Assert.Single(process.Requests);

        release.SetResult(Result(Answer()));
        Assert.NotNull(await first);
        Assert.Single(process.Requests);

        Assert.NotNull(await adapter.ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));
        Assert.Equal(2, process.Requests.Count);
    }

    // ---- The real process boundary, against the isolated fixture (no gh, no account, no network). ----

    private Task WriteBehaviorAsync(object behavior) =>
        File.WriteAllTextAsync(Path.Combine(_scratch, "gh-api-behavior.json"), JsonSerializer.Serialize(behavior));

    private GitHubCheckContextsObservationAdapter CreateReal(TimeSpan? timeout = null, Func<string, string?>? environment = null) =>
        new(new ChildProcessExecutionAdapter(), () => _scratch, environment ?? (_ => null), timeout);

    private JsonElement ReadInvocation() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(_scratch, "gh-api-invocation.json"))).RootElement.Clone();

    /// <summary>Fifty entries with maximal identifiers and 256-byte labels, every label character escaped as a six-byte sequence, pretty
    /// printed with two-space indentation: the largest answer the contract supports.</summary>
    private static (string Json, string[] Names) MaximalAnswer()
    {
        var names = new string[50];
        var nodes = new List<string>();
        for (var index = 0; index < 50; index++)
        {
            names[index] = new string((char)('a' + (index % 26)), 256);
            var id = index.ToString("D3") + new string('I', 253);
            var suite = index.ToString("D3") + new string('S', 253);
            var label = string.Concat(Enumerable.Repeat(U(names[index][0]), 256));
            nodes.Add(
                "        {\n          \"__typename\": \"CheckRun\",\n          \"id\": \"" + id + "\",\n          \"name\": \"" + label
                + "\",\n          \"status\": \"COMPLETED\",\n          \"conclusion\": \"SUCCESS\",\n          \"checkSuite\": {\n"
                + "            \"id\": \"" + suite + "\"\n          }\n        }");
        }

        var json = "{\n  \"data\": {\n    \"repository\": {\n      \"id\": \"" + NodeId + "\",\n      \"name\": \"" + Repository
            + "\",\n      \"nameWithOwner\": \"" + Owner + "/" + Repository + "\",\n      \"ref\": {\n        \"name\": \"" + Branch
            + "\",\n        \"prefix\": \"refs/heads/\",\n        \"target\": {\n          \"__typename\": \"Commit\",\n          \"oid\": \""
            + Sha + "\",\n          \"statusCheckRollup\": {\n            \"contexts\": {\n              \"totalCount\": 51,\n"
            + "              \"pageInfo\": {\n                \"hasNextPage\": true\n              },\n              \"nodes\": [\n"
            + string.Join(",\n", nodes) + "\n              ]\n            }\n          }\n        }\n      }\n    }\n  }\n}\n";
        return (json, names);
    }

    [Fact]
    public async Task A_real_child_receives_exactly_the_fixed_argv_and_no_ambient_environment_and_the_contexts_are_observed()
    {
        Assert.True(File.Exists(FixtureExecutablePath));
        await WriteBehaviorAsync(new { Stdout = WithNodes(RunNode(), StatusNode()) });
        var ambient = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GH_TOKEN"] = "sentinel-token",
            ["GH_HOST"] = "evil.example",
            ["TEMP"] = Path.GetTempPath(),
        };

        // The fixture's mode is its first argument ("api"), exactly the first argument the adapter passes.
        var probe = await CreateReal(environment: name => ambient.GetValueOrDefault(name))
            .ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None);

        AssertPage(probe, 2, Run(), Status());
        var invocation = ReadInvocation();
        Assert.Equal(
            [
                "api", "graphql", "--hostname", "github.com", "--method", "POST",
                "-f", "query=" + ExpectedQuery, "-f", $"owner={Owner}", "-f", $"name={Repository}", "-f", $"ref=refs/heads/{Branch}",
            ],
            invocation.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!).ToArray());
        var names = invocation.GetProperty("environment").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Equals("GH_TOKEN", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Equals("GH_HOST", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name.Equals("GH_PROMPT_DISABLED", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("sentinel-token", invocation.GetProperty("environment").GetRawText());
    }

    [Theory]
    [MemberData(nameof(SecretShapeClasses))]
    public async Task A_real_child_that_prints_a_supported_secret_shape_literally_or_escaped_yields_only_redacted_labels(string shapeClass)
    {
        Assert.True(File.Exists(FixtureExecutablePath));
        var shape = SecretShape(shapeClass);
        foreach (var spelling in ShapeSpellings(shape))
        {
            // The real executor redacts the literal spelling in the raw text; only the adapter can see the escaped spellings decoded.
            await WriteBehaviorAsync(new
            {
                Stdout = WithNodes(RunNode(id: "CR_1", name: "build " + spelling + " done"), StatusNode(id: "SC_1", context: "ci " + spelling)),
            });

            var probe = await CreateReal().ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None);

            Assert.True(probe is not null, $"{shapeClass}: the answer was refused");
            Assert.Equal(2, probe!.Page!.Contexts.Count);
            AssertLabel(probe.Page.Contexts[0].Name, "build " + Redacted + " done", shapeClass + " check run");
            AssertLabel(probe.Page.Contexts[1].Name, "ci " + Redacted, shapeClass + " status context");
            Assert.True(!probe.Page.Contexts.Any(context => context.Name.Contains(shape, StringComparison.Ordinal)), "a label carries a supported secret shape");
        }
    }

    [Fact]
    public async Task The_largest_supported_answer_passes_the_real_executor_intact_below_the_per_stream_cap_and_decodes_every_label()
    {
        var (json, names) = MaximalAnswer();
        var bytes = Encoding.UTF8.GetByteCount(json);
        Assert.InRange(bytes, 100_000, StreamCap - 1);
        await WriteBehaviorAsync(new { Stdout = json });

        var probe = await CreateReal().ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None);

        Assert.NotNull(probe);
        Assert.Equal(51, probe.Page!.TotalCount);
        Assert.Equal(50, probe.Page.Contexts.Count);
        Assert.Equal(names, probe.Page.Contexts.Select(context => context.Name));
        Assert.All(probe.Page.Contexts, context =>
        {
            Assert.Equal(256, context.Id.Length);
            Assert.Equal(256, context.CheckSuiteId!.Length);
            Assert.Equal(256, Encoding.UTF8.GetByteCount(context.Name));
        });
    }

    [Fact]
    public async Task An_answer_of_exactly_the_per_stream_cap_is_admitted_and_one_byte_more_is_truncated_and_refused()
    {
        var (json, _) = MaximalAnswer();
        var padded = json + new string(' ', StreamCap - Encoding.UTF8.GetByteCount(json));
        Assert.Equal(StreamCap, Encoding.UTF8.GetByteCount(padded));
        await WriteBehaviorAsync(new { Stdout = padded });

        Assert.NotNull(await CreateReal().ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None));

        await WriteBehaviorAsync(new { Stdout = padded + " " });

        Assert.Null(await CreateReal().ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Theory]
    [InlineData(StreamCap + 1)]
    [InlineData(StreamCap * 2)]
    public async Task Real_oversized_output_is_truncated_by_the_cap_and_refused(int bytes)
    {
        await WriteBehaviorAsync(new { StdoutBytes = bytes });

        Assert.Null(await CreateReal().ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Fact]
    public async Task A_valid_answer_beside_a_standard_error_over_its_cap_is_refused_because_the_truncated_stream_is_never_trusted()
    {
        var (json, _) = MaximalAnswer();
        await WriteBehaviorAsync(new { Stdout = json, Stderr = new string('e', StreamCap + 1) });

        Assert.Null(await CreateReal().ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None));

        await WriteBehaviorAsync(new { Stdout = json, Stderr = new string('e', StreamCap) });

        Assert.NotNull(await CreateReal().ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Fact]
    public async Task A_real_nonzero_exit_is_refused()
    {
        await WriteBehaviorAsync(new { Stderr = "HTTP 404", ExitCode = 1 });

        Assert.Null(await CreateReal().ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Fact]
    public async Task A_real_timeout_is_refused_and_the_child_is_gone()
    {
        var pidFile = Path.Combine(_scratch, "pid.txt");
        await WriteBehaviorAsync(new { SleepMs = 60_000, PidFile = pidFile });

        var probe = await CreateReal(TimeSpan.FromMilliseconds(500))
            .ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None);

        Assert.Null(probe);
        AssertProcessGone(int.Parse(await File.ReadAllTextAsync(pidFile)));
    }

    [Fact]
    public async Task Real_cancellation_raises_kills_the_child_and_releases_the_slot()
    {
        var pidFile = Path.Combine(_scratch, "pid.txt");
        await WriteBehaviorAsync(new { SleepMs = 60_000, PidFile = pidFile });
        var adapter = CreateReal(TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();

        var observing = adapter.ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, cancellation.Token);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(pidFile) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observing);
        AssertProcessGone(int.Parse(await File.ReadAllTextAsync(pidFile)));

        await WriteBehaviorAsync(new { Stdout = Answer() });
        Assert.NotNull(await adapter.ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None));
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
