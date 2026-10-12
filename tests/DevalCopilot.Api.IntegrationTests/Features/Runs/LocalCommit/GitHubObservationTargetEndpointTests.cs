using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0040 through the protected HTTP boundary: a real delivery written by the production handlers and supervisor against real Git
/// and file-backed SQLite, then an explicit Human selection of its GitHub observation scope. Every fact asserted is compared with
/// the durable rows, and the selection is proven to add exactly one target and one Human event and to change nothing else.
/// </summary>
public sealed class GitHubObservationTargetEndpointTests : GitHubObservationTargetTestBase
{
    private static readonly string[] ReadKeys = ["runId", "source", "state", "target"];

    private static readonly string[] WriteKeys = ["runId", "source", "target"];

    private static readonly string[] SourceKeys = ["branchName", "commitSha", "operationId"];

    private static readonly string[] ScopeKeys = ["eventId", "host", "id", "owner", "recordedAtUtc", "repository"];

    private async Task<(LocalCommitHost Host, LocalCommitLineageIds Ids, Guid OperationId, string Sha)> DeliveredHostAsync(
        Func<ILocalCommitRepository, ILocalCommitRepository>? decorate = null,
        Func<ILocalCommitPreparer, ILocalCommitPreparer>? decoratePreparer = null)
    {
        LocalCommitLineageIds ids;
        Guid operationId;
        using (var delivering = StartHost(runSupervisor: true))
        {
            (ids, operationId) = await DeliverAsync(delivering);
        }

        var host = StartHost(runSupervisor: false, decorate, decoratePreparer);
        return (host, ids, operationId, await CommitShaOfAsync(operationId));
    }

    [Fact]
    public async Task Recording_a_target_adds_exactly_one_target_and_one_human_event_and_touches_nothing_else_or_any_external_system()
    {
        var tripwire = new ExternalCallTripwire();
        var (host, ids, operationId, sha) = await DeliveredHostAsync(
            repository => tripwire.Wrap<ILocalCommitRepository>(repository),
            preparer => tripwire.Wrap<ILocalCommitPreparer>(preparer));
        tripwire.Arm();
        Assert.Equal((0, 0), await CountsAsync(ids.RunId));
        var databaseBefore = await FingerprintWithoutTargetWritesAsync();
        var mainBefore = Scene.MainRepositoryFingerprint();
        var workspaceBefore = Scene.RunWorkspaceGit("status", "--porcelain=v1", "-z");
        var tipBefore = Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim();
        var storageBefore = Scene.StorageLeaves();

        var before = await GetTargetAsync(host, ids.RunId);
        Assert.Equal(HttpStatusCode.OK, before.Status);
        Assert.Equal("NotRecorded", before.State);
        Assert.Equal(ReadKeys, KeysOf(before.Root));
        Assert.Equal(JsonValueKind.Null, before.Root.GetProperty("target").ValueKind);
        var source = before.Root.GetProperty("source");
        Assert.Equal(SourceKeys, KeysOf(source));
        Assert.Equal(operationId, source.GetProperty("operationId").GetGuid());
        Assert.Equal(sha, source.GetProperty("commitSha").GetString());
        Assert.Equal(Scene.BranchName, source.GetProperty("branchName").GetString());
        Assert.Equal(tipBefore, sha);

        var posted = await PostTargetAsync(host, ids.RunId, BodyOf(operationId, sha));

        Assert.Equal(HttpStatusCode.OK, posted.Status);
        Assert.Equal(WriteKeys, KeysOf(posted.Root));
        Assert.Equal(ids.RunId, posted.Root.GetProperty("runId").GetGuid());
        Assert.Equal(SourceKeys, KeysOf(posted.Root.GetProperty("source")));
        var scope = posted.Root.GetProperty("target");
        Assert.Equal(ScopeKeys, KeysOf(scope));
        Assert.Equal("github.com", scope.GetProperty("host").GetString());
        Assert.Equal(DefaultOwner, scope.GetProperty("owner").GetString());
        Assert.Equal(DefaultRepository, scope.GetProperty("repository").GetString());

        Assert.Equal((1, 1), await CountsAsync(ids.RunId));
        var row = await TargetRowAsync(ids.RunId);
        var recordedEvent = await TargetEventRowAsync(ids.RunId);
        Assert.Equal(row.Id, scope.GetProperty("id").GetGuid());
        Assert.Equal(row.EventId, scope.GetProperty("eventId").GetGuid());
        Assert.Equal(recordedEvent.Id, row.EventId);
        Assert.Equal(row.RecordedAtUtc, scope.GetProperty("recordedAtUtc").GetDateTimeOffset());
        Assert.Equal(row.RecordedAtUtc, recordedEvent.OccurredAtUtc);
        Assert.Equal(ids.ProjectId, row.ProjectId);
        Assert.Equal(ids.WorkspaceId, row.GitWorkspaceId);
        Assert.Equal(operationId, row.LocalCommitOperationId);
        Assert.Equal(sha, row.CommitSha);
        Assert.Equal(Scene.BranchName, row.BranchName);
        Assert.Equal(DefaultOwner, row.Owner);
        Assert.Equal(DefaultRepository, row.Repository);
        Assert.Equal(ParticipantKind.Human, recordedEvent.ActorKind);
        Assert.Null(recordedEvent.ActorAgentRole);
        Assert.Null(recordedEvent.ActorAgentProvider);
        Assert.Null(recordedEvent.AttemptId);
        Assert.Equal(GitHubObservationTargetEventPayload.Build(row), recordedEvent.PayloadJson);

        var after = await GetTargetAsync(host, ids.RunId);
        Assert.Equal("Available", after.State);
        Assert.Equal(ReadKeys, KeysOf(after.Root));
        Assert.Equal(posted.Root.GetProperty("source").ToString(), after.Root.GetProperty("source").ToString());
        Assert.Equal(scope.ToString(), after.Root.GetProperty("target").ToString());

        Assert.Equal(databaseBefore, await FingerprintWithoutTargetWritesAsync());
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        Assert.Equal(workspaceBefore, Scene.RunWorkspaceGit("status", "--porcelain=v1", "-z"));
        Assert.Equal(tipBefore, Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim());
        Assert.Equal(storageBefore, Scene.StorageLeaves());
        Assert.Empty(tripwire.Calls);
        Assert.DoesNotContain(Scene.MainPath, posted.Body + after.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Scene.WorkspacePath, posted.Body + after.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reading_the_route_writes_no_row_event_lock_or_file_in_any_state()
    {
        var tripwire = new ExternalCallTripwire();
        var (host, ids, operationId, sha) = await DeliveredHostAsync(
            repository => tripwire.Wrap<ILocalCommitRepository>(repository),
            preparer => tripwire.Wrap<ILocalCommitPreparer>(preparer));
        tripwire.Arm();
        var fingerprint = await DatabaseFingerprintAsync();
        var main = Scene.MainRepositoryFingerprint();

        for (var read = 0; read < 3; read++)
        {
            Assert.Equal("NotRecorded", (await GetTargetAsync(host, ids.RunId)).State);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await GetTargetAsync(host, Guid.NewGuid())).Status);
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());

        await RecordAsync(host, ids.RunId, operationId, sha);
        var recorded = await DatabaseFingerprintAsync();
        for (var read = 0; read < 3; read++)
        {
            Assert.Equal("Available", (await GetTargetAsync(host, ids.RunId)).State);
        }

        Assert.Equal(recorded, await DatabaseFingerprintAsync());
        Assert.Equal(main, Scene.MainRepositoryFingerprint());
        Assert.Empty(tripwire.Calls);
    }

    [Fact]
    public async Task The_same_selection_replays_the_original_record_time_and_event_without_another_insert()
    {
        var (host, ids, operationId, sha) = await DeliveredHostAsync();
        var first = await RecordAsync(host, ids.RunId, operationId, sha);
        var rowBefore = await TargetRowAsync(ids.RunId);
        var eventBefore = await TargetEventRowAsync(ids.RunId);
        var fingerprint = await DatabaseFingerprintAsync();

        var second = await RecordAsync(host, ids.RunId, operationId, sha);
        await Task.Delay(30);
        var third = await RecordAsync(host, ids.RunId, operationId, sha);

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal((1, 1), await CountsAsync(ids.RunId));
        Assert.Equal(rowBefore.Id, (await TargetRowAsync(ids.RunId)).Id);
        Assert.Equal(eventBefore.Sequence, (await TargetEventRowAsync(ids.RunId)).Sequence);
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
    }

    [Theory]
    [InlineData("other-owner", DefaultRepository)]
    [InlineData(DefaultOwner, "other-repo")]
    [InlineData("Octo-Cat", DefaultRepository)]
    [InlineData(DefaultOwner, "Hello.World")]
    public async Task A_different_selection_conflicts_and_leaves_the_original_selection_unchanged(string owner, string repository)
    {
        var (host, ids, operationId, sha) = await DeliveredHostAsync();
        var original = await RecordAsync(host, ids.RunId, operationId, sha);
        var fingerprint = await DatabaseFingerprintAsync();

        var different = await PostTargetAsync(host, ids.RunId, BodyOf(operationId, sha, owner, repository));

        Assert.Equal(HttpStatusCode.Conflict, different.Status);
        Assert.Equal("github_observation_target.conflict", different.Root.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.DoesNotContain(owner + repository, different.Body, StringComparison.Ordinal);
        Assert.Equal((1, 1), await CountsAsync(ids.RunId));
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal(original, await RecordAsync(host, ids.RunId, operationId, sha));
    }

    [Fact]
    public async Task Fields_outside_the_four_allowed_are_never_input_and_the_branch_comes_from_the_pinned_rows()
    {
        var (host, ids, operationId, sha) = await DeliveredHostAsync();
        var body = "{\"operationId\":\"" + operationId + "\",\"commitSha\":\"" + sha + "\",\"owner\":\"octo-cat\",\"repository\":\"hello.world\","
            + "\"branchName\":\"evil-branch\",\"host\":\"example.org\",\"url\":\"https://example.org/x/y\",\"token\":\"secret-value\"}";

        var posted = await PostTargetAsync(host, ids.RunId, body);

        Assert.Equal(HttpStatusCode.OK, posted.Status);
        Assert.Equal(Scene.BranchName, posted.Root.GetProperty("source").GetProperty("branchName").GetString());
        Assert.Equal("github.com", posted.Root.GetProperty("target").GetProperty("host").GetString());
        var stored = await TargetEventRowAsync(ids.RunId);
        Assert.DoesNotContain("evil-branch", stored.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", posted.Body + stored.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("example.org", posted.Body + stored.PayloadJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"operationId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("not json")]
    public async Task A_malformed_or_incomplete_body_is_a_bad_request_that_writes_nothing(string body)
    {
        var (host, ids, _, _) = await DeliveredHostAsync();

        var response = await PostTargetAsync(host, ids.RunId, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal((0, 0), await CountsAsync(ids.RunId));
    }

    [Theory]
    [InlineData("sha", "ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD", "octo-cat", "hello.world")]
    [InlineData("sha", "abcdefabcdefabcdefabcdefabcdefabcdefabc", "octo-cat", "hello.world")]
    [InlineData("sha", "abcdefabcdefabcdefabcdefabcdefabcdefabcde1", "octo-cat", "hello.world")]
    [InlineData("sha", "SECRETgggggggggggggggggggggggggggggggg", "octo-cat", "hello.world")]
    [InlineData("exact", "", " octo-cat", "hello.world")]
    [InlineData("exact", "", "octo-cat ", "hello.world")]
    [InlineData("exact", "", "-octo", "hello.world")]
    [InlineData("exact", "", "octo-", "hello.world")]
    [InlineData("exact", "", "octo_cat", "hello.world")]
    [InlineData("exact", "", "octo-cat-SECRETowner-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "hello.world")]
    [InlineData("exact", "", "https://github.com/octo-cat", "hello.world")]
    [InlineData("exact", "", "octo-cat", "")]
    [InlineData("exact", "", "octo-cat", ".hidden")]
    [InlineData("exact", "", "octo-cat", "hello world SECRETrepo")]
    [InlineData("exact", "", "octo-cat", "hello.world\n")]
    [InlineData("exact", "", "octo-cat", "https://github.com/octo-cat/hello")]
    [InlineData("exact", "", "octo-cat", "répo")]
    public async Task An_invalid_value_is_a_fixed_bad_request_that_is_never_echoed_and_writes_nothing(
        string kind, string commitSha, string owner, string repository)
    {
        var (host, ids, operationId, sha) = await DeliveredHostAsync();
        var submittedSha = kind == "sha" ? commitSha : sha;

        var response = await PostTargetAsync(host, ids.RunId, BodyOf(operationId, submittedSha, owner, repository));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.invalid", response.Root.GetProperty("errors")[0].GetProperty("code").GetString());
        foreach (var rejected in new[] { commitSha, owner, repository }.Where(value => value.Length > 3))
        {
            Assert.DoesNotContain(rejected, response.Body, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("SECRET", response.Body, StringComparison.Ordinal);
        Assert.Equal((0, 0), await CountsAsync(ids.RunId));
    }

    [Fact]
    public async Task The_longest_supported_names_are_accepted_and_one_character_more_is_not()
    {
        var (host, ids, operationId, sha) = await DeliveredHostAsync();
        var tooLong = await PostTargetAsync(host, ids.RunId, BodyOf(operationId, sha, new string('a', 40), DefaultRepository));
        var repositoryTooLong = await PostTargetAsync(host, ids.RunId, BodyOf(operationId, sha, DefaultOwner, new string('r', 101)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.Status);
        Assert.Equal(HttpStatusCode.BadRequest, repositoryTooLong.Status);
        Assert.Equal((0, 0), await CountsAsync(ids.RunId));

        var exact = await PostTargetAsync(host, ids.RunId, BodyOf(operationId, sha, new string('a', 39), new string('r', 100)));

        Assert.Equal(HttpStatusCode.OK, exact.Status);
        Assert.Equal(new string('a', 39), exact.Root.GetProperty("target").GetProperty("owner").GetString());
    }

    [Fact]
    public async Task An_undelivered_run_has_no_source_and_no_target_can_be_recorded_for_it()
    {
        LocalCommitLineageIds undelivered;
        using (var seeding = StartHost(runSupervisor: false))
        {
            undelivered = await LocalCommitLineage.SeedAsync(seeding, Scene);
        }

        using var host = StartHost(runSupervisor: false);
        var read = await GetTargetAsync(host, undelivered.RunId);
        Assert.Equal(HttpStatusCode.OK, read.Status);
        Assert.Equal("NotRecorded", read.State);
        Assert.Equal(JsonValueKind.Null, read.Root.GetProperty("source").ValueKind);
        Assert.Equal(JsonValueKind.Null, read.Root.GetProperty("target").ValueKind);

        var fingerprint = await DatabaseFingerprintAsync();
        var response = await PostTargetAsync(host, undelivered.RunId, BodyOf(Guid.NewGuid(), new string('a', 40)));

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("github_observation_target.unavailable", response.Root.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal((0, 0), await CountsAsync(undelivered.RunId));
    }

    [Fact]
    public async Task A_different_operation_or_commit_than_the_recorded_delivery_is_unavailable_and_writes_nothing()
    {
        var (host, ids, operationId, sha) = await DeliveredHostAsync();
        var fingerprint = await DatabaseFingerprintAsync();

        foreach (var body in new[]
                 {
                     BodyOf(Guid.NewGuid(), sha),
                     BodyOf(operationId, Nines40),
                     BodyOf(operationId, Scene.BaselineCommit),
                     BodyOf(Guid.NewGuid(), Nines40),
                 })
        {
            var response = await PostTargetAsync(host, ids.RunId, body);
            Assert.Equal(HttpStatusCode.Conflict, response.Status);
            Assert.Equal("github_observation_target.unavailable", response.Root.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.DoesNotContain(Nines40, response.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(sha, response.Body, StringComparison.Ordinal);
        }

        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal((0, 0), await CountsAsync(ids.RunId));
    }

    [Fact]
    public async Task An_unknown_run_is_the_fixed_not_found_for_both_operations()
    {
        using var host = StartHost(runSupervisor: false);

        var post = await PostTargetAsync(host, Guid.NewGuid(), BodyOf(Guid.NewGuid(), new string('a', 40)));
        var get = await GetTargetAsync(host, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, post.Status);
        Assert.Equal(HttpStatusCode.NotFound, get.Status);
        Assert.Equal("github_observation_target.not_found", post.Root.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal("github_observation_target.not_found", get.Root.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Both_operations_require_authentication_and_only_get_and_post_are_routed()
    {
        var (host, ids, operationId, sha) = await DeliveredHostAsync();
        var fingerprint = await DatabaseFingerprintAsync();

        using (var anonymous = host.CreateClient())
        {
            using var content = new StringContent(BodyOf(operationId, sha), Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/runs/{ids.RunId}/{TargetRoute}", content)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/runs/{ids.RunId}/{TargetRoute}")).StatusCode);
        }

        using var wrong = host.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync($"/api/runs/{ids.RunId}/{TargetRoute}")).StatusCode);

        using var client = LocalCommitLineage.AuthenticatedClient(host);
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            using var request = new HttpRequestMessage(method, $"/api/runs/{ids.RunId}/{TargetRoute}")
            {
                Content = new StringContent(BodyOf(operationId, sha), Encoding.UTF8, "application/json"),
            };
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.SendAsync(request)).StatusCode);
        }

        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal((0, 0), await CountsAsync(ids.RunId));
    }

    private static int FreeLoopbackPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact]
    public async Task The_real_host_refuses_a_body_beyond_eight_kibibytes_and_still_reaches_the_handler_inside_it()
    {
        await using var kestrel = new ApiWebApplicationFactory();
        kestrel.UseKestrel(FreeLoopbackPort());
        kestrel.StartServer();
        using var client = kestrel.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        var run = Guid.NewGuid();
        var body = BodyOf(Guid.NewGuid(), new string('a', 40));

        using var oversized = new StringContent(body[..^1] + ",\"padding\":\"" + new string('x', 9 * 1024) + "\"}", Encoding.UTF8, "application/json");
        using var inside = new StringContent(body[..^1] + ",\"padding\":\"" + new string('x', 4 * 1024) + "\"}", Encoding.UTF8, "application/json");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync($"/api/runs/{run}/{TargetRoute}", oversized)).StatusCode);
        var accepted = await client.PostAsync($"/api/runs/{run}/{TargetRoute}", inside);
        Assert.Equal(HttpStatusCode.NotFound, accepted.StatusCode);
        Assert.Contains("github_observation_target.not_found", await accepted.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
