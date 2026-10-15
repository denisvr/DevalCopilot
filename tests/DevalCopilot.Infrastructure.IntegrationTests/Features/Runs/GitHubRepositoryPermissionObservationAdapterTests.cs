using System.Diagnostics;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.Runs;

/// <summary>
/// The GitHub repository permission observer (ADR-0049). Request shape, environment, strict response admission of the six
/// <c>RepositoryPermission</c> literals and the explicit null, single-flight and fault propagation are proved against a recording process
/// double; the real process boundary (argv, environment, caps, timeout, cancellation and cleanup) against the isolated
/// <c>ProcessExecutionFixture</c> "api" double. A fixture proves the process boundary only: it never proves how the installed GitHub CLI
/// formats a GraphQL answer, how stored authentication behaves or that GitHub is reliable. No real <c>gh</c>, account, credential or
/// network is ever used.
/// </summary>
public sealed class GitHubRepositoryPermissionObservationAdapterTests : IDisposable
{
    private static readonly string FixtureExecutablePath = Path.Combine(AppContext.BaseDirectory, "ProcessExecutionFixture.exe");
    private const string GhPath = @"C:\safe\gh.exe";
    private const string Owner = "octo-cat";
    private const string Repository = "hello.world";

    // Restated independently of the production constant: the exact read-only query the child must be asked for.
    private const string ExpectedQuery =
        "query($owner:String!,$name:String!){repository(owner:$owner,name:$name){id name nameWithOwner viewerPermission}}";

    private readonly string _scratch;

    public GitHubRepositoryPermissionObservationAdapterTests()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"devalcopilot-gh-permission-tests-{Guid.NewGuid():N}");
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

    /// <summary>The whole answer with the permission passed as raw JSON, so null, wrong types and strange literals are all expressible.</summary>
    private static string Answer(
        string id = "R_kgDOExample01", string name = Repository, string? nameWithOwner = null, string permissionJson = "\"WRITE\"") =>
        "{\"data\":{\"repository\":{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"nameWithOwner\":\""
        + (nameWithOwner ?? $"{Owner}/{name}") + "\",\"viewerPermission\":" + permissionJson + "}}}\n";

    private static string Wrap(string members) => "{\"data\":{\"repository\":{" + members + "}}}";

    private const string IdentityMembers = "\"id\":\"R_x\",\"name\":\"hello.world\",\"nameWithOwner\":\"octo-cat/hello.world\"";

    private GitHubRepositoryPermissionObservationAdapter Create(
        IProcessExecutionAdapter process, Func<string, string?>? environment = null, TimeSpan? timeout = null) =>
        new(process, () => _scratch, environment ?? (_ => null), timeout);

    private Task<GitHubRepositoryPermissionProbe?> ObserveStdoutAsync(
        string stdout, int exitCode = 0, string owner = Owner, string repository = Repository)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(stdout, exitCode)));
        return Create(process).ObserveAsync(GhPath, owner, repository, CancellationToken.None);
    }

    [Fact]
    public async Task The_request_is_one_literal_argv_with_raw_string_variables_inside_the_host_scratch_with_the_fixed_caps_and_no_sinks()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(Answer())));

        await Create(process).ObserveAsync(GhPath, Owner, Repository, CancellationToken.None);

        var request = Assert.Single(process.Requests);
        Assert.Equal(GhPath, request.ExecutablePath);
        Assert.Equal(
            [
                "api", "graphql", "--hostname", "github.com", "--method", "POST",
                "-f", "query=" + ExpectedQuery, "-f", $"owner={Owner}", "-f", $"name={Repository}",
            ],
            request.Arguments);
        Assert.Equal(ExpectedQuery, GitHubRepositoryPermissionObservationAdapter.Query);
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
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains("mutation", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("123", "456")]
    [InlineData("a1", "1e3")]
    [InlineData("octo-cat", "repo.git")]
    [InlineData("o", "a.b_c-d.git")]
    public async Task Numeric_looking_names_and_a_git_suffix_are_passed_exactly_as_stored_and_never_interpolated(string owner, string repository)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(
            Result(Answer(name: repository, nameWithOwner: owner + "/" + repository))));

        var probe = await Create(process).ObserveAsync(GhPath, owner, repository, CancellationToken.None);

        var arguments = Assert.Single(process.Requests).Arguments;
        Assert.Equal(["-f", $"owner={owner}", "-f", $"name={repository}"], arguments.Skip(arguments.Count - 4));
        Assert.Equal("query=" + ExpectedQuery, arguments[7]);
        Assert.Equal(new GitHubRepositoryPermissionProbe("R_kgDOExample01", owner, repository, "WRITE"), probe);
    }

    [Theory]
    [InlineData("", "repo")]
    [InlineData("owner", "")]
    [InlineData("-owner", "repo")]
    [InlineData("owner-", "repo")]
    [InlineData("own er", "repo")]
    [InlineData("owner", ".repo")]
    [InlineData("owner", "re po")]
    [InlineData("owner", "re/po")]
    [InlineData("own/er", "repo")]
    [InlineData("owner", "repo\n")]
    [InlineData("owner", "repo\"}){x")]
    [InlineData("own_er", "repo")]
    [InlineData("own\u00e9r", "repo")]
    public async Task A_stored_name_outside_the_supported_subset_is_never_passed_to_a_process(string owner, string repository)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(Answer())));

        Assert.Null(await Create(process).ObserveAsync(GhPath, owner, repository, CancellationToken.None));
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

        await Create(process, name => ambient.GetValueOrDefault(name)).ObserveAsync(GhPath, Owner, Repository, CancellationToken.None);

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
    [InlineData("READ")]
    [InlineData("TRIAGE")]
    [InlineData("TRIAGE_PLUS")]
    [InlineData("WRITE")]
    [InlineData("MAINTAIN")]
    [InlineData("ADMIN")]
    public async Task Each_of_the_six_literals_is_admitted_exactly_as_returned_and_never_ranked_or_translated(string permission)
    {
        var probe = await ObserveStdoutAsync(Answer(permissionJson: "\"" + permission + "\""));

        Assert.Equal(new GitHubRepositoryPermissionProbe("R_kgDOExample01", Owner, Repository, permission), probe);
    }

    [Fact]
    public async Task An_explicit_null_permission_is_admitted_as_an_absent_permission_with_the_identity_and_is_not_a_failure()
    {
        var probe = await ObserveStdoutAsync(Answer(permissionJson: "null"));

        Assert.Equal(new GitHubRepositoryPermissionProbe("R_kgDOExample01", Owner, Repository, null), probe);
    }

    // The pipe stands for nothing special here; the values are plain strings that are not exactly one of the six literals.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("read")]
    [InlineData("Read")]
    [InlineData("write")]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("triage_plus")]
    [InlineData("TRIAGE PLUS")]
    [InlineData("TRIAGE-PLUS")]
    [InlineData("TRIAGEPLUS")]
    [InlineData("TRIAGE+")]
    [InlineData("READ ")]
    [InlineData(" READ")]
    [InlineData("READ\n")]
    [InlineData("TRIAGE_PLUS ")]
    [InlineData("OWNER")]
    [InlineData("NONE")]
    [InlineData("PUSH")]
    [InlineData("PULL")]
    [InlineData("CAN_PUSH")]
    [InlineData("WRITER")]
    [InlineData("MAINTAINER")]
    [InlineData("ADMINISTRATOR")]
    [InlineData("READ|WRITE")]
    [InlineData("READ,WRITE")]
    [InlineData("\u0420EAD")]
    [InlineData("ADMIN\u200b")]
    public async Task An_unknown_or_differently_spelled_permission_is_refused_whole(string permission)
    {
        Assert.Null(await ObserveStdoutAsync(Answer(permissionJson: "\"" + permission.Replace("\n", "\\n", StringComparison.Ordinal) + "\"")));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("[]")]
    [InlineData("[\"WRITE\"]")]
    [InlineData("{}")]
    [InlineData("{\"permission\":\"WRITE\"}")]
    public async Task A_permission_that_is_not_a_string_or_an_explicit_null_is_refused_whole(string permissionJson)
    {
        Assert.Null(await ObserveStdoutAsync(Answer(permissionJson: permissionJson)));
    }

    // A pipe stands for the backslash so the source holds no raw JSON escape sequence.
    [Theory]
    [InlineData("data", "|u0064ata")]
    [InlineData("repository", "|u0072epository")]
    [InlineData("id", "|u0069d")]
    [InlineData("name", "|u006eame")]
    [InlineData("nameWithOwner", "name|u0057ithOwner")]
    [InlineData("viewerPermission", "viewer|u0050ermission")]
    [InlineData("viewerPermission", "|u0076iewerPermission")]
    public async Task An_escaped_property_name_at_any_level_is_refused_whole_while_the_literal_answer_is_admitted(
        string key, string escapedKey)
    {
        var literal = Answer();
        Assert.NotNull(await ObserveStdoutAsync(literal));

        var mutated = literal.Replace($"\"{key}\":", $"\"{escapedKey.Replace('|', '\\')}\":", StringComparison.Ordinal);

        Assert.NotEqual(literal, mutated);
        Assert.Null(await ObserveStdoutAsync(mutated));
        Assert.Null(await ObserveStdoutAsync(mutated.Replace("\"data\":{", "\"data\": {", StringComparison.Ordinal)));
    }

    // An escaped permission value would decode to an admitted literal, so only the whole-response refusal of any backslash stops it.
    [Theory]
    [InlineData("WR|u0049TE")]
    [InlineData("|u0057RITE")]
    [InlineData("TRIAGE|u005fPLUS")]
    [InlineData("AD|u004dIN")]
    [InlineData("READ|u0020")]
    public async Task An_escaped_permission_value_that_would_decode_to_an_admitted_literal_is_refused_whole(string escaped)
    {
        var mutated = Answer(permissionJson: "\"" + escaped.Replace('|', '\\') + "\"");

        Assert.Contains('\\', mutated);
        Assert.Null(await ObserveStdoutAsync(mutated));
    }

    [Fact]
    public async Task An_escape_in_any_other_value_is_refused_whole()
    {
        Assert.Null(await ObserveStdoutAsync(Answer(id: "R_k\\u0067")));
        Assert.Null(await ObserveStdoutAsync(Answer(name: "hello\\u002eworld", nameWithOwner: "octo-cat/hello.world")));
        Assert.Null(await ObserveStdoutAsync(Answer(nameWithOwner: "octo\\u002dcat/hello.world")));
    }

    [Fact]
    public async Task Whitespace_and_property_order_controls_stay_admitted_beside_the_escape_refusal()
    {
        var reordered = "{ \"data\" : { \"repository\" : { \"viewerPermission\" : \"MAINTAIN\", \"nameWithOwner\" : \"octo-cat/hello.world\","
            + " \"name\" : \"hello.world\", \"id\" : \"R_kgDOExample01\" } } }\r\n";

        Assert.Equal(
            new GitHubRepositoryPermissionProbe("R_kgDOExample01", Owner, Repository, "MAINTAIN"), await ObserveStdoutAsync(reordered));
    }

    [Fact]
    public async Task A_coherent_answer_is_the_observed_identity_with_the_names_exactly_as_returned()
    {
        var probe = await ObserveStdoutAsync(
            "  {\n \"data\": { \"repository\": { \"name\": \"hello.world\", \"viewerPermission\": \"ADMIN\", "
            + "\"nameWithOwner\": \"octo-cat/hello.world\", \"id\": \"R_kgDOExample01\" } } }\r\n");

        Assert.Equal(new GitHubRepositoryPermissionProbe("R_kgDOExample01", Owner, Repository, "ADMIN"), probe);
    }

    [Fact]
    public async Task Names_that_differ_from_the_stored_scope_only_in_ascii_case_are_accepted_and_reported_as_returned()
    {
        var probe = await ObserveStdoutAsync(Answer(name: "Hello.World", nameWithOwner: "OCTO-cat/Hello.World"));

        Assert.Equal(new GitHubRepositoryPermissionProbe("R_kgDOExample01", "OCTO-cat", "Hello.World", "WRITE"), probe);
    }

    [Theory]
    [InlineData("someone-else", "hello.world")]
    [InlineData("octo-cat", "renamed.repo")]
    [InlineData("octo-cat", "hello.world.git")]
    [InlineData("octo-cat2", "hello.world")]
    public async Task A_foreign_or_renamed_repository_is_never_adopted(string owner, string repository)
    {
        Assert.Null(await ObserveStdoutAsync(Answer(name: repository, nameWithOwner: $"{owner}/{repository}")));
    }

    [Fact]
    public async Task A_stored_git_suffix_is_never_stripped_when_comparing()
    {
        Assert.Null(await ObserveStdoutAsync(Answer(name: "hello", nameWithOwner: "octo-cat/hello"), repository: "hello.git"));
        Assert.Null(await ObserveStdoutAsync(Answer(name: "hello.git", nameWithOwner: "octo-cat/hello.git"), repository: "hello"));
    }

    [Theory]
    [InlineData("R")]
    [InlineData("R_kgDOabc-def/ghi+jkl=")]
    [InlineData("MDEwOlJlcG9zaXRvcnkx")]
    public async Task A_conservative_opaque_id_is_admitted_without_decoding(string id)
    {
        Assert.Equal(id, (await ObserveStdoutAsync(Answer(id: id)))?.NodeId);
    }

    [Fact]
    public async Task An_id_at_the_length_bound_is_admitted_and_one_beyond_it_is_not()
    {
        Assert.NotNull(await ObserveStdoutAsync(Answer(id: new string('a', 256))));
        Assert.Null(await ObserveStdoutAsync(Answer(id: new string('a', 257))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("R kg")]
    [InlineData("R.kg")]
    [InlineData("R:kg")]
    [InlineData("R\tkg")]
    [InlineData("R_kg\u00e9")]
    public async Task An_id_outside_the_conservative_character_set_is_refused(string id)
    {
        Assert.Null(await ObserveStdoutAsync(Answer(id: id)));
    }

    [Theory]
    [InlineData("""{"data":{"repository":null}}""")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"data":{"repository":{}}}""")]
    [InlineData("""{"data":{"repository":"x"}}""")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"data":{"repository":[]}}""")]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    [InlineData("\"x\"")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("""{"errors":[{"message":"Could not resolve to a Repository"}],"data":{"repository":null}}""")]
    [InlineData("octo-cat/hello.world\nR_x")]
    public async Task Every_malformed_envelope_is_refused_whole(string stdout)
    {
        Assert.Null(await ObserveStdoutAsync(stdout));
    }

    [Fact]
    public async Task A_missing_inaccessible_or_partial_repository_member_is_refused_whole()
    {
        Assert.Null(await ObserveStdoutAsync(Wrap(IdentityMembers)));
        Assert.Null(await ObserveStdoutAsync(Wrap("\"id\":\"R_x\",\"name\":\"hello.world\",\"viewerPermission\":\"WRITE\"")));
        Assert.Null(await ObserveStdoutAsync(Wrap("\"id\":\"R_x\",\"nameWithOwner\":\"octo-cat/hello.world\",\"viewerPermission\":\"WRITE\"")));
        Assert.Null(await ObserveStdoutAsync(Wrap("\"name\":\"hello.world\",\"nameWithOwner\":\"octo-cat/hello.world\",\"viewerPermission\":\"WRITE\"")));
        Assert.Null(await ObserveStdoutAsync(Wrap(IdentityMembers + ",\"other\":\"WRITE\"")));
    }

    [Theory]
    [InlineData(",\"url\":\"https://x\"")]
    [InlineData(",\"isPrivate\":true")]
    [InlineData(",\"viewerCanAdminister\":true")]
    [InlineData(",\"viewerPermission\":\"WRITE\"")]
    [InlineData(",\"viewerPermission\":null")]
    [InlineData(",\"id\":\"R_x\"")]
    [InlineData(",\"name\":\"hello.world\"")]
    [InlineData(",\"nameWithOwner\":\"octo-cat/hello.world\"")]
    public async Task An_extra_or_duplicate_repository_member_is_refused_whole(string extra)
    {
        Assert.Null(await ObserveStdoutAsync(Wrap(IdentityMembers + ",\"viewerPermission\":\"WRITE\"" + extra)));
    }

    [Fact]
    public async Task A_duplicate_data_or_repository_member_and_foreign_top_level_members_are_refused_whole()
    {
        var repository = "{" + IdentityMembers + ",\"viewerPermission\":\"WRITE\"}";

        Assert.NotNull(await ObserveStdoutAsync("{\"data\":{\"repository\":" + repository + "}}"));
        Assert.Null(await ObserveStdoutAsync("{\"data\":{\"repository\":" + repository + "},\"data\":{\"repository\":" + repository + "}}"));
        Assert.Null(await ObserveStdoutAsync("{\"data\":{\"repository\":" + repository + ",\"repository\":" + repository + "}}"));
        Assert.Null(await ObserveStdoutAsync("{\"data\":{\"repository\":" + repository + "},\"errors\":[{\"message\":\"x\"}]}"));
        Assert.Null(await ObserveStdoutAsync("{\"data\":{\"repository\":" + repository + "},\"extensions\":{\"cost\":1}}"));
        Assert.Null(await ObserveStdoutAsync("{\"data\":{\"repository\":" + repository + ",\"viewer\":{\"login\":\"x\"}}}"));
    }

    [Theory]
    [InlineData("""{"data":{"repository":{"id":null,"name":"hello.world","nameWithOwner":"octo-cat/hello.world","viewerPermission":"WRITE"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":null,"nameWithOwner":"octo-cat/hello.world","viewerPermission":"WRITE"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":null,"viewerPermission":"WRITE"}}}""")]
    [InlineData("""{"data":{"repository":{"id":1,"name":"hello.world","nameWithOwner":"octo-cat/hello.world","viewerPermission":"WRITE"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"hello.world","viewerPermission":"WRITE"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"/hello.world","viewerPermission":"WRITE"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/","viewerPermission":"WRITE"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/x/hello.world","viewerPermission":"WRITE"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/other","viewerPermission":"WRITE"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/HELLO.WORLD","viewerPermission":"WRITE"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":" octo-cat/hello.world","viewerPermission":"WRITE"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world ","nameWithOwner":"octo-cat/hello.world ","viewerPermission":"WRITE"}}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo%2Dcat/hello.world","viewerPermission":"WRITE"}}}""")]
    public async Task Malformed_identity_members_are_refused_whole_even_beside_a_valid_permission(string stdout)
    {
        Assert.Null(await ObserveStdoutAsync(stdout));
    }

    [Theory]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","viewerPermission":"WRITE"}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","viewerPermission":"WRITE"}}} {"data":{}}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","viewerPermission":"WRITE"}}} trailing""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","viewerPermission":"WRITE"}},}""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","viewerPermission":"WRITE"}}} // comment""")]
    [InlineData("""{"data":{"repository":{"id":"R_x","name":"hello.world","nameWithOwner":"octo-cat/hello.world","viewerPermission":"WRITE",}}}""")]
    public async Task Trailing_content_comments_commas_and_truncated_json_are_refused_whole(string stdout)
    {
        Assert.Null(await ObserveStdoutAsync(stdout));
    }

    [Fact]
    public async Task Nesting_beyond_the_configured_depth_is_refused()
    {
        var deep = string.Concat(Enumerable.Repeat("[", 9)) + string.Concat(Enumerable.Repeat("]", 9));

        Assert.Null(await ObserveStdoutAsync(Answer(permissionJson: deep)));
        Assert.Null(await ObserveStdoutAsync("{\"data\":{\"repository\":{" + IdentityMembers + ",\"viewerPermission\":\"WRITE\",\"x\":" + deep + "}}}"));
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
    [InlineData(4)]
    [InlineData(-1)]
    public async Task A_nonzero_exit_is_unknown_even_with_a_valid_looking_body(int exitCode)
    {
        Assert.Null(await ObserveStdoutAsync(Answer(), exitCode));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_truncated_stream_is_refused_even_with_a_valid_looking_body(bool stdoutTruncated, bool stderrTruncated)
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(
            Result(Answer(), stdoutTruncated: stdoutTruncated, stderrTruncated: stderrTruncated)));

        Assert.Null(await Create(process).ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));
    }

    [Fact]
    public async Task Stderr_content_never_changes_the_outcome_and_is_never_returned()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(
            Result(Answer(), stderr: "HTTP 404 Not Found ghp_sentinel")));

        var probe = await Create(process).ObserveAsync(GhPath, Owner, Repository, CancellationToken.None);

        Assert.NotNull(probe);
        Assert.DoesNotContain("sentinel", probe.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timed_out_process_is_refused()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(Answer(), outcome: ProcessExecutionOutcome.TimedOut)));

        Assert.Null(await Create(process).ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));
    }

    [Fact]
    public async Task An_os_process_start_failure_is_refused_and_exposes_no_exception_detail()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromException<ProcessExecutionResult>(
            new System.ComponentModel.Win32Exception(5, @"cannot start C:\secret\path ghp_sentinel")));

        Assert.Null(await Create(process).ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));
    }

    [Fact]
    public async Task A_vetted_executable_that_vanished_is_refused()
    {
        var process = new RecordingProcessAdapter((_, _) => Task.FromException<ProcessExecutionResult>(
            new ArgumentException(@"does not exist C:\secret\path", nameof(ProcessExecutionRequest.ExecutablePath))));

        Assert.Null(await Create(process).ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));
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
            () => Create(process).ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));

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

        await Assert.ThrowsAnyAsync<Exception>(() => adapter.ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));

        Assert.NotNull(await adapter.ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));
    }

    [Fact]
    public async Task A_scratch_directory_fault_propagates_and_releases_the_slot()
    {
        var calls = 0;
        var process = new RecordingProcessAdapter((_, _) => Task.FromResult(Result(Answer())));
        var adapter = new GitHubRepositoryPermissionObservationAdapter(
            process, () => ++calls == 1 ? throw new IOException("scratch") : _scratch, _ => null);

        await Assert.ThrowsAsync<IOException>(() => adapter.ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));

        Assert.NotNull(await adapter.ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));
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
            () => adapter.ObserveAsync(GhPath, Owner, Repository, cancellation.Token));

        Assert.NotNull(await adapter.ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));
    }

    [Fact]
    public async Task A_thrown_cancellation_propagates_and_releases_the_slot()
    {
        var calls = 0;
        var process = new RecordingProcessAdapter((_, _) =>
            ++calls == 1 ? throw new OperationCanceledException() : Task.FromResult(Result(Answer())));
        var adapter = Create(process);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));

        Assert.NotNull(await adapter.ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));
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

        var first = adapter.ObserveAsync(GhPath, Owner, Repository, CancellationToken.None);
        await started.Task;
        var concurrent = await adapter.ObserveAsync(GhPath, Owner, Repository, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(concurrent);
        Assert.Single(process.Requests);

        release.SetResult(Result(Answer()));
        Assert.NotNull(await first);
        Assert.Single(process.Requests);

        Assert.NotNull(await adapter.ObserveAsync(GhPath, Owner, Repository, CancellationToken.None));
        Assert.Equal(2, process.Requests.Count);
    }

    // --- The real process boundary, against the isolated fixture (no gh, no account, no network). ---

    private Task WriteBehaviorAsync(object behavior) =>
        File.WriteAllTextAsync(Path.Combine(_scratch, "gh-api-behavior.json"), JsonSerializer.Serialize(behavior));

    private GitHubRepositoryPermissionObservationAdapter CreateReal(TimeSpan? timeout = null, Func<string, string?>? environment = null) =>
        new(new ChildProcessExecutionAdapter(), () => _scratch, environment ?? (_ => null), timeout);

    private JsonElement ReadInvocation() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(_scratch, "gh-api-invocation.json"))).RootElement.Clone();

    [Fact]
    public async Task A_real_child_receives_exactly_the_fixed_argv_and_no_ambient_environment_and_the_permission_is_observed()
    {
        Assert.True(File.Exists(FixtureExecutablePath));
        await WriteBehaviorAsync(new { Stdout = Answer(permissionJson: "\"TRIAGE_PLUS\"") });
        var ambient = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GH_TOKEN"] = "sentinel-token",
            ["GH_HOST"] = "evil.example",
            ["TEMP"] = Path.GetTempPath(),
        };

        // The fixture's mode is its first argument ("api"), exactly the first argument the adapter passes.
        var probe = await CreateReal(environment: name => ambient.GetValueOrDefault(name))
            .ObserveAsync(FixtureExecutablePath, Owner, Repository, CancellationToken.None);

        Assert.Equal(new GitHubRepositoryPermissionProbe("R_kgDOExample01", Owner, Repository, "TRIAGE_PLUS"), probe);
        var invocation = ReadInvocation();
        Assert.Equal(
            [
                "api", "graphql", "--hostname", "github.com", "--method", "POST",
                "-f", "query=" + ExpectedQuery, "-f", $"owner={Owner}", "-f", $"name={Repository}",
            ],
            invocation.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!).ToArray());
        var names = invocation.GetProperty("environment").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Equals("GH_TOKEN", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Equals("GH_HOST", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name.Equals("GH_PROMPT_DISABLED", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("sentinel-token", invocation.GetProperty("environment").GetRawText());
    }

    [Fact]
    public async Task A_real_explicit_null_permission_is_an_observation_with_no_permission()
    {
        await WriteBehaviorAsync(new { Stdout = Answer(permissionJson: "null") });

        var probe = await CreateReal().ObserveAsync(FixtureExecutablePath, Owner, Repository, CancellationToken.None);

        Assert.Equal(new GitHubRepositoryPermissionProbe("R_kgDOExample01", Owner, Repository, null), probe);
    }

    [Fact]
    public async Task A_real_nonzero_exit_is_refused()
    {
        await WriteBehaviorAsync(new { Stderr = "HTTP 404", ExitCode = 1 });

        Assert.Null(await CreateReal().ObserveAsync(FixtureExecutablePath, Owner, Repository, CancellationToken.None));
    }

    [Theory]
    [InlineData(8 * 1024 + 1)]
    [InlineData(64 * 1024)]
    public async Task Real_oversized_output_is_truncated_by_the_cap_and_refused(int bytes)
    {
        await WriteBehaviorAsync(new { StdoutBytes = bytes });

        Assert.Null(await CreateReal().ObserveAsync(FixtureExecutablePath, Owner, Repository, CancellationToken.None));
    }

    [Fact]
    public async Task A_real_timeout_is_refused_and_the_child_is_gone()
    {
        var pidFile = Path.Combine(_scratch, "pid.txt");
        await WriteBehaviorAsync(new { SleepMs = 60_000, PidFile = pidFile });

        var probe = await CreateReal(TimeSpan.FromMilliseconds(500))
            .ObserveAsync(FixtureExecutablePath, Owner, Repository, CancellationToken.None);

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

        var observing = adapter.ObserveAsync(FixtureExecutablePath, Owner, Repository, cancellation.Token);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(pidFile) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observing);
        AssertProcessGone(int.Parse(await File.ReadAllTextAsync(pidFile)));

        await WriteBehaviorAsync(new { Stdout = Answer() });
        Assert.NotNull(await adapter.ObserveAsync(FixtureExecutablePath, Owner, Repository, CancellationToken.None));
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
