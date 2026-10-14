using System.Diagnostics;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// The GitHub commit check observer (ADR-0044). Request shape, environment, strict whole-response admission (including the refusal of
/// GitHub's short-name fallback and of every unsupported rollup state), single-flight and fault propagation are proved against a recording
/// process double; the real process boundary (argv, environment, caps, timeout, cancellation and cleanup) against the isolated
/// <c>ProcessExecutionFixture</c> "api" double. A fixture proves the process boundary only: it never proves how the installed GitHub CLI
/// formats a GraphQL answer, how stored authentication behaves or that GitHub is reliable. No real <c>gh</c>, account, credential or network
/// is ever used.
/// </summary>
public sealed class GitHubCiObservationAdapterTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");
    private const string GhPath = @"C:\safe\gh.exe";
    private const string Owner = "octo-cat";
    private const string Repository = "hello.world";
    private const string Branch = "devalcopilot/workspace/0123456789abcdef0123456789abcdef/3";
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    // Restated independently of the production constant: the exact read-only query the child must be asked for.
    private const string ExpectedQuery =
        "query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner "
        + "ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid statusCheckRollup{state}}}}}}";

    private readonly string _scratch;

    public GitHubCiObservationAdapterTests()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"devalcopilot-gh-ci-tests-{Guid.NewGuid():N}");
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

    private static string Rollup(string? state) => state is null ? "null" : "{\"state\":\"" + state + "\"}";

    private static string RefJson(
        string name = Branch, string prefix = "refs/heads/", string typeName = "Commit", string oid = Sha, string rollup = "null") =>
        "{\"name\":\"" + name + "\",\"prefix\":\"" + prefix + "\",\"target\":{\"__typename\":\"" + typeName + "\",\"oid\":\"" + oid
        + "\",\"statusCheckRollup\":" + rollup + "}}";

    private static string Answer(
        string id = "R_kgDOExample01", string name = Repository, string? nameWithOwner = null, string? reference = null) =>
        "{\"data\":{\"repository\":{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"nameWithOwner\":\""
        + (nameWithOwner ?? $"{Owner}/{name}") + "\",\"ref\":" + (reference ?? RefJson()) + "}}}\n";

    private static string WithRef(string reference) => Answer(reference: reference);

    private static string WithTarget(string target) =>
        WithRef("{\"name\":\"" + Branch + "\",\"prefix\":\"refs/heads/\",\"target\":" + target + "}");

    private static string WithRollup(string rollup) => WithRef(RefJson(rollup: rollup));

    private GitHubCiObservationAdapter Create(
        IProcessExecutionAdapter process, Func<string, string?>? environment = null, TimeSpan? timeout = null) =>
        new(process, () => _scratch, environment ?? (_ => null), timeout);

    private Task<GitHubCiProbe?> ObserveStdoutAsync(
        string stdout, int exitCode = 0, string owner = Owner, string repository = Repository, string branch = Branch)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(stdout, exitCode)));
        return Create(process).ObserveAsync(GhPath, owner, repository, branch, CancellationToken.None);
    }

    [Fact]
    public async Task The_request_is_one_literal_argv_with_raw_string_variables_inside_the_host_scratch_with_the_fixed_caps_and_no_sinks()
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
        Assert.Equal(ExpectedQuery, GitHubCiObservationAdapter.Query);
        Assert.Equal(_scratch, request.WorkingDirectory);
        Assert.Equal(_scratch, request.ApprovedRoot);
        Assert.Equal(TimeSpan.FromSeconds(15), request.Timeout);
        Assert.Equal(8 * 1024, request.MaxBytesPerStream);
        Assert.Equal(16 * 1024, request.MaxTotalCapturedBytes);
        Assert.Null(request.StandardOutputSinkPath);
        Assert.Null(request.StandardErrorSinkPath);
        Assert.Null(request.StandardInput);
        Assert.DoesNotContain("-F", request.Arguments);
        Assert.DoesNotContain("--jq", request.Arguments);
        Assert.DoesNotContain("--paginate", request.Arguments);
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
    [InlineData("feature/x")]
    [InlineData("refs/heads/main")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/0")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/03")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/-3")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/+3")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3 ")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3/4")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/2147483648")]
    [InlineData("devalcopilot/workspace/0123456789ABCDEF0123456789abcdef/3")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcde/3")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef0/3")]
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

    [Theory]
    [InlineData("ERROR")]
    [InlineData("EXPECTED")]
    [InlineData("FAILURE")]
    [InlineData("PENDING")]
    [InlineData("SUCCESS")]
    public async Task Each_of_the_five_literal_rollup_states_is_admitted_exactly_as_returned_with_the_exact_ref(string state)
    {
        var probe = await ObserveStdoutAsync(WithRollup(Rollup(state)));

        Assert.Equal(new GitHubCiProbe("R_kgDOExample01", Owner, Repository, new GitHubCiRef(Branch, Sha), state), probe);
    }

    [Fact]
    public async Task A_null_rollup_is_admitted_as_no_rollup_returned_with_the_exact_ref_and_nothing_translated()
    {
        var probe = await ObserveStdoutAsync(WithRollup("null"));

        Assert.Equal(new GitHubCiProbe("R_kgDOExample01", Owner, Repository, new GitHubCiRef(Branch, Sha), null), probe);
    }

    [Fact]
    public async Task A_returned_ref_is_admitted_whatever_the_property_order_or_spacing()
    {
        var probe = await ObserveStdoutAsync(
            "  {\n \"data\": { \"repository\": { \"ref\": { \"target\": { \"statusCheckRollup\": { \"state\": \"PENDING\" }, \"oid\": \"" + Sha
            + "\", \"__typename\": \"Commit\" }, \"prefix\": \"refs/heads/\", \"name\": \"" + Branch + "\" },"
            + " \"nameWithOwner\": \"octo-cat/hello.world\", \"name\": \"hello.world\", \"id\": \"R_kgDOExample01\" } } }\r\n");

        Assert.Equal(new GitHubCiProbe("R_kgDOExample01", Owner, Repository, new GitHubCiRef(Branch, Sha), "PENDING"), probe);
    }

    [Fact]
    public async Task An_explicit_null_ref_is_admitted_as_no_ref_and_nothing_more()
    {
        var probe = await ObserveStdoutAsync(Answer(reference: "null"));

        Assert.Equal(new GitHubCiProbe("R_kgDOExample01", Owner, Repository, null, null), probe);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("Success")]
    [InlineData("SUCCESS ")]
    [InlineData(" SUCCESS")]
    [InlineData("PASSED")]
    [InlineData("GREEN")]
    [InlineData("APPROVED")]
    [InlineData("COMPLETED")]
    [InlineData("QUEUED")]
    [InlineData("IN_PROGRESS")]
    [InlineData("NEUTRAL")]
    [InlineData("")]
    public async Task An_unsupported_rollup_state_refuses_the_whole_answer_never_a_translated_or_defaulted_state(string state)
    {
        Assert.Null(await ObserveStdoutAsync(WithRollup(Rollup(state))));
    }

    [Theory]
    [InlineData("""{"state":null}""")]
    [InlineData("""{"state":1}""")]
    [InlineData("""{"state":true}""")]
    [InlineData("""{"state":["SUCCESS"]}""")]
    [InlineData("""{"state":{"value":"SUCCESS"}}""")]
    [InlineData("""{}""")]
    [InlineData("""{"state":"SUCCESS","contexts":{"totalCount":1}}""")]
    [InlineData("""{"state":"SUCCESS","id":"x"}""")]
    [InlineData("""{"state":"SUCCESS","state":"SUCCESS"}""")]
    [InlineData("""{"state":"SUCCESS","state":"FAILURE"}""")]
    [InlineData("""{"status":"SUCCESS"}""")]
    [InlineData("""{"State":"SUCCESS"}""")]
    [InlineData("""{"state":"SUCC\u0045SS"}""")]
    [InlineData("""{"st\u0061te":"SUCCESS"}""")]
    [InlineData("""[]""")]
    [InlineData("""["SUCCESS"]""")]
    [InlineData("\"SUCCESS\"")]
    [InlineData("false")]
    [InlineData("true")]
    [InlineData("0")]
    public async Task A_malformed_or_ambiguous_rollup_object_refuses_the_whole_answer(string rollup)
    {
        Assert.Null(await ObserveStdoutAsync(WithRollup(rollup)));
    }

    [Theory]
    [InlineData("""{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567"}""")]
    [InlineData("""{"__typename":"Commit","statusCheckRollup":null}""")]
    [InlineData("""{"oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}""")]
    [InlineData("""{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null,"message":"x"}""")]
    [InlineData("""{"__typename":"Commit","__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}""")]
    [InlineData("""{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}""")]
    [InlineData("""{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null,"statusCheckRollup":null}""")]
    [InlineData("""{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":{"state":"SUCCESS"},"statusCheckRollup":null}""")]
    [InlineData("""{"__typename":null,"oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}""")]
    [InlineData("""{"__typename":"Commit","oid":null,"statusCheckRollup":null}""")]
    [InlineData("""{"__typename":"Commit","oid":1234,"statusCheckRollup":null}""")]
    [InlineData("""{"__typename":"Tag"}""")]
    [InlineData("""{"__typename":"Tree"}""")]
    [InlineData("""{"__typename":"Blob"}""")]
    [InlineData("""{"__typename":"Tag","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}""")]
    [InlineData("""{"__typename":"commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}""")]
    [InlineData("""{"__typename":"","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}""")]
    [InlineData("""{}""")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"0123456789abcdef0123456789abcdef01234567\"")]
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
    public async Task A_foreign_or_renamed_repository_is_never_adopted_even_with_a_valid_ref_and_rollup(string owner, string repository)
    {
        Assert.Null(await ObserveStdoutAsync(Answer(
            name: repository, nameWithOwner: $"{owner}/{repository}", reference: RefJson(rollup: Rollup("SUCCESS")))));
        Assert.Null(await ObserveStdoutAsync(Answer(name: repository, nameWithOwner: $"{owner}/{repository}", reference: "null")));
    }

    [Theory]
    [InlineData("R_kgDOabc-def/ghi+jkl=", true)]
    [InlineData("R", true)]
    [InlineData("", false)]
    [InlineData("R kg", false)]
    [InlineData("R.kg", false)]
    [InlineData("R_k\\u0067", false)]
    public async Task The_opaque_node_id_follows_the_conservative_policy_without_decoding(string id, bool admitted)
    {
        Assert.Equal(admitted, (await ObserveStdoutAsync(Answer(id: id))) is not null);
    }

    [Fact]
    public async Task A_non_ascii_node_id_is_refused()
    {
        var nonAscii = "R_kg" + char.ConvertFromUtf32(0xE9);

        Assert.Null(await ObserveStdoutAsync(Answer(id: nonAscii)));
    }

    [Theory]
    [InlineData("v1", "refs/tags/", "Commit")]
    [InlineData("v1", "refs/heads/", "Commit")]
    [InlineData("main", "refs/heads/", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/4", "refs/heads/", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/tags/", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/remotes/", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "REFS/HEADS/", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "", "Commit")]
    [InlineData("refs/heads/devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads/", "Commit")]
    [InlineData("DEVALCOPILOT/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads/", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3 ", "refs/heads/", "Commit")]
    [InlineData("", "refs/heads/", "Commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads/", "Tag")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads/", "Tree")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads/", "Blob")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads/", "commit")]
    [InlineData("devalcopilot/workspace/0123456789abcdef0123456789abcdef/3", "refs/heads/", "")]
    public async Task A_ref_that_is_not_exactly_the_recorded_branch_head_commit_is_refused_even_with_a_success_rollup(
        string name, string prefix, string typeName)
    {
        Assert.Null(await ObserveStdoutAsync(WithRef(RefJson(name, prefix, typeName, rollup: Rollup("SUCCESS")))));
    }

    [Theory]
    [InlineData("0123456789ABCDEF0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456")]
    [InlineData("0123456789abcdef0123456789abcdef012345678")]
    [InlineData("0123456789abcdef0123456789abcdef0123456g")]
    [InlineData("0123456789abcdef0123456789abcdef0123456 ")]
    [InlineData(" 0123456789abcdef0123456789abcdef0123456")]
    [InlineData("")]
    [InlineData("refs/heads/main")]
    [InlineData("0123456789abcdef0123456789abcdef0123456\\u0037")]
    [InlineData("0123456789abcdef0123456789abcdef0123456\\n")]
    public async Task An_object_id_that_is_not_forty_lowercase_hexadecimal_characters_is_refused(string oid)
    {
        Assert.Null(await ObserveStdoutAsync(WithRef(RefJson(oid: oid, rollup: Rollup("SUCCESS")))));
    }

    [Fact]
    public async Task A_second_branch_of_the_same_repository_with_a_different_workspace_is_not_the_recorded_branch()
    {
        Assert.Null(await ObserveStdoutAsync(
            WithRef(RefJson(name: "devalcopilot/workspace/0123456789abcdef0123456789abcdef/30", rollup: Rollup("SUCCESS")))));
    }

    [Theory]
    [InlineData("""{"name":"devalcopilot/workspace/0123456789abcdef0123456789abcdef/3","prefix":"refs/heads/"}""")]
    [InlineData("""{"prefix":"refs/heads/","target":{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}}""")]
    [InlineData("""{"name":"devalcopilot/workspace/0123456789abcdef0123456789abcdef/3","target":{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}}""")]
    [InlineData("""{"name":"devalcopilot/workspace/0123456789abcdef0123456789abcdef/3","name":"devalcopilot/workspace/0123456789abcdef0123456789abcdef/3","prefix":"refs/heads/","target":{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}}""")]
    [InlineData("""{"name":"devalcopilot/workspace/0123456789abcdef0123456789abcdef/3","prefix":"refs/heads/","prefix":"refs/heads/","target":{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}}""")]
    [InlineData("""{"name":"devalcopilot/workspace/0123456789abcdef0123456789abcdef/3","prefix":"refs/heads/","target":{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null},"target":{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}}""")]
    [InlineData("""{"name":"devalcopilot/workspace/0123456789abcdef0123456789abcdef/3","prefix":"refs/heads/","target":{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null},"id":"x"}""")]
    [InlineData("""{"name":"devalcopilot\/workspace\/0123456789abcdef0123456789abcdef\/3","prefix":"refs\/heads\/","target":{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}}""")]
    [InlineData("""{"name":"devalcopilot/workspace/0123456789abcdef0123456789abcdef/\u0033","prefix":"refs/heads/","target":{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}}""")]
    [InlineData("""{"name":"devalcopilot/workspace/0123456789abcdef0123456789abcdef/3","prefix":"refs/heads/","target":{"__typename":"Comm\u0069t","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}}""")]
    [InlineData("""{"name":3,"prefix":"refs/heads/","target":{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}}""")]
    [InlineData("""{"name":"devalcopilot/workspace/0123456789abcdef0123456789abcdef/3","prefix":null,"target":{"__typename":"Commit","oid":"0123456789abcdef0123456789abcdef01234567","statusCheckRollup":null}}""")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"refs/heads/main\"")]
    [InlineData("false")]
    [InlineData("0")]
    public async Task A_malformed_or_ambiguous_ref_object_is_refused_whole(string reference)
    {
        Assert.Null(await ObserveStdoutAsync(WithRef(reference)));
    }

    [Theory]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null,"url":"https://x"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","nameWithOwner":"octo-cat/hello.world","ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null,"ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}},"errors":[{"message":"x"}]}""")]
    [InlineData("""{"errors":[{"message":"x"}],"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}},"extensions":{"cost":1}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null},"viewer":{"login":"x"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}},"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}}}""")]
    [InlineData("""{"data":{"repository":null}}""")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"data":{"repository":{}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":"x"}}}""")]
    [InlineData("""{"data":{"repository":{"id":null,"name":"hello.world","nameWithOwner":"octo-cat/hello.world","ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world ","ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"hello.world","ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/x/hello.world","ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/other","ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/HELLO.WORLD","ref":null}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo\u002dcat/hello.world","ref":null}}}""")]
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

    [Theory]
    [InlineData("data", "\\u0064ata")]
    [InlineData("repository", "\\u0072epository")]
    [InlineData("id", "\\u0069d")]
    [InlineData("nameWithOwner", "name\\u0057ithOwner")]
    [InlineData("ref", "\\u0072ef")]
    [InlineData("prefix", "pre\\u0066ix")]
    [InlineData("target", "\\u0074arget")]
    [InlineData("oid", "\\u006fid")]
    [InlineData("__typename", "__type\\u006eame")]
    [InlineData("statusCheckRollup", "statusCheckRoll\\u0075p")]
    [InlineData("state", "st\\u0061te")]
    [InlineData("name", "\\u006eame")]
    public async Task An_escaped_property_name_at_any_level_is_refused_whole_while_the_literal_answer_is_admitted(string key, string escaped)
    {
        var literal = Answer(reference: RefJson(rollup: Rollup("SUCCESS")));
        Assert.NotNull(await ObserveStdoutAsync(literal));
        Assert.NotNull(await ObserveStdoutAsync(Answer(reference: "null")));

        var mutated = literal.Replace($"\"{key}\":", $"\"{escaped}\":", StringComparison.Ordinal);

        Assert.NotEqual(literal, mutated);
        Assert.Null(await ObserveStdoutAsync(mutated));
        Assert.Null(await ObserveStdoutAsync(mutated.Replace("\"data\":{", "\"data\": {", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_backslash_anywhere_in_the_output_refuses_the_whole_answer_even_inside_an_otherwise_valid_state_value()
    {
        Assert.NotNull(await ObserveStdoutAsync(WithRollup(Rollup("SUCCESS"))));
        Assert.Null(await ObserveStdoutAsync(WithRollup("{\"state\":\"SUCC\\u0045SS\"}")));
        Assert.Null(await ObserveStdoutAsync(WithRollup(Rollup("SUCCESS")).Replace("octo-cat", "octo\\u002dcat", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Whitespace_and_property_order_controls_stay_admitted_beside_the_escape_refusal()
    {
        var reordered = "{ \"data\" : { \"repository\" : { \"ref\" : { \"target\" : { \"statusCheckRollup\" : null, \"oid\" : \"" + Sha
            + "\", \"__typename\" : \"Commit\" }, \"prefix\" : \"refs/heads/\", \"name\" : \"" + Branch + "\" },"
            + " \"nameWithOwner\" : \"octo-cat/hello.world\", \"name\" : \"hello.world\", \"id\" : \"R_kgDOExample01\" } } }\n";

        Assert.Equal(new GitHubCiRef(Branch, Sha), (await ObserveStdoutAsync(reordered))?.Ref);
        Assert.Null((await ObserveStdoutAsync(Answer(reference: "null")))?.Ref);
    }

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
        Assert.Null(await ObserveStdoutAsync(WithRollup(Rollup("SUCCESS")), exitCode));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_truncated_stream_is_refused_even_with_a_valid_looking_body(bool stdoutTruncated, bool stderrTruncated)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(
            Result(Answer(), stdoutTruncated: stdoutTruncated, stderrTruncated: stderrTruncated)));

        Assert.Null(await Create(process).ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Fact]
    public async Task Stderr_content_never_changes_the_outcome_and_is_never_returned()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(
            Result(Answer(), stderr: "HTTP 404 Not Found ghp_sentinel")));

        var probe = await Create(process).ObserveAsync(GhPath, Owner, Repository, Branch, CancellationToken.None);

        Assert.NotNull(probe);
        Assert.DoesNotContain("sentinel", probe.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timed_out_process_is_refused()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(Answer(), outcome: ProcessExecutionOutcome.TimedOut)));

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
        var adapter = new GitHubCiObservationAdapter(
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

    // --- The real process boundary, against the isolated fixture (no gh, no account, no network). ---

    private Task WriteBehaviorAsync(object behavior) =>
        File.WriteAllTextAsync(Path.Combine(_scratch, "gh-api-behavior.json"), JsonSerializer.Serialize(behavior));

    private GitHubCiObservationAdapter CreateReal(TimeSpan? timeout = null, Func<string, string?>? environment = null) =>
        new(new ChildProcessExecutionAdapter(), () => _scratch, environment ?? (_ => null), timeout);

    private JsonElement ReadInvocation() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(_scratch, "gh-api-invocation.json"))).RootElement.Clone();

    [Fact]
    public async Task A_real_child_receives_exactly_the_fixed_argv_and_no_ambient_environment_and_the_check_summary_is_observed()
    {
        Assert.True(File.Exists(FixtureExecutablePath));
        await WriteBehaviorAsync(new { Stdout = WithRollup(Rollup("PENDING")) });
        var ambient = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GH_TOKEN"] = "sentinel-token",
            ["GH_HOST"] = "evil.example",
            ["TEMP"] = Path.GetTempPath(),
        };

        // The fixture's mode is its first argument ("api"), exactly the first argument the adapter passes.
        var probe = await CreateReal(environment: name => ambient.GetValueOrDefault(name))
            .ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None);

        Assert.Equal(new GitHubCiProbe("R_kgDOExample01", Owner, Repository, new GitHubCiRef(Branch, Sha), "PENDING"), probe);
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

    [Fact]
    public async Task A_real_nonzero_exit_is_refused()
    {
        await WriteBehaviorAsync(new { Stderr = "HTTP 404", ExitCode = 1 });

        Assert.Null(await CreateReal().ObserveAsync(FixtureExecutablePath, Owner, Repository, Branch, CancellationToken.None));
    }

    [Theory]
    [InlineData(8 * 1024 + 1)]
    [InlineData(64 * 1024)]
    public async Task Real_oversized_output_is_truncated_by_the_cap_and_refused(int bytes)
    {
        await WriteBehaviorAsync(new { StdoutBytes = bytes });

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
