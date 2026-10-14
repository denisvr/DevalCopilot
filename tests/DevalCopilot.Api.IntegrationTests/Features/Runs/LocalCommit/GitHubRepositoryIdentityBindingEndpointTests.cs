using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0043 through the production host: a real delivered run and its recorded ADR-0040 target, a scripted provider-neutral identity
/// observer, and the real MVC authentication, mediator, readers and file-backed SQLite. The write is proved to add exactly one binding
/// and one Human event and to touch nothing else; every refusal is proved to write nothing, echo nothing and invoke the observer exactly
/// when (and only with what) the recorded scope and the current CLI snapshot admit.
/// </summary>
public sealed class GitHubRepositoryIdentityBindingEndpointTests : GitHubRepositoryIdentityBindingTestBase
{
    [Fact]
    public async Task Binding_adds_exactly_one_binding_and_one_human_event_touches_nothing_else_and_observes_exactly_once()
    {
        var delivered = await DeliveredAsync();
        var before = await FingerprintWithoutBindingWritesAsync();
        var started = DateTimeOffset.UtcNow;

        var posted = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));

        Assert.Equal(HttpStatusCode.OK, posted.Status);
        Assert.Equal(ResponseKeys, KeysOf(posted.Root));
        Assert.Equal(delivered.Ids.RunId, posted.Root.GetProperty("runId").GetGuid());
        Assert.Equal(["branchName", "commitSha", "operationId"], KeysOf(posted.Root.GetProperty("source")));
        Assert.Equal(delivered.OperationId, posted.Root.GetProperty("source").GetProperty("operationId").GetGuid());
        Assert.Equal(delivered.Sha, posted.Root.GetProperty("source").GetProperty("commitSha").GetString());
        Assert.Equal(Scene.BranchName, posted.Root.GetProperty("source").GetProperty("branchName").GetString());
        Assert.Equal(["eventId", "host", "id", "owner", "recordedAtUtc", "repository"], KeysOf(posted.Root.GetProperty("target")));
        Assert.Equal(delivered.TargetId, posted.Root.GetProperty("target").GetProperty("id").GetGuid());
        var binding = posted.Root.GetProperty("binding");
        Assert.Equal(BindingKeys, KeysOf(binding));
        Assert.Equal(NodeId, binding.GetProperty("repositoryNodeId").GetString());
        Assert.Equal(DefaultOwner, binding.GetProperty("owner").GetString());
        Assert.Equal(DefaultRepository, binding.GetProperty("repository").GetString());

        var call = Assert.Single(delivered.Observer.Calls);
        Assert.Equal((GhPath, DefaultOwner, DefaultRepository), call);
        Assert.True(Assert.Single(delivered.Observer.Tokens).CanBeCanceled);
        Assert.Equal((1, 1), await BindingCountsAsync(delivered.Ids.RunId));
        Assert.Equal(before, await FingerprintWithoutBindingWritesAsync());
        Assert.DoesNotContain(GhPath, posted.Body, StringComparison.OrdinalIgnoreCase);

        var row = await BindingRowAsync(delivered.Ids.RunId);
        var target = await TargetRowAsync(delivered.Ids.RunId);
        var stored = await BindingEventRowAsync(delivered.Ids.RunId);
        Assert.Equal(row.Id, binding.GetProperty("id").GetGuid());
        Assert.Equal(row.EventId, binding.GetProperty("eventId").GetGuid());
        Assert.Equal(stored.Id, row.EventId);
        Assert.Equal(delivered.TargetId, row.ObservationTargetId);
        Assert.Equal(delivered.Sha, row.CommitSha);
        Assert.Equal(Scene.BranchName, row.BranchName);
        Assert.True(target.RecordedAtUtc <= row.ObservedAtUtc && row.ObservedAtUtc <= row.RecordedAtUtc);
        Assert.InRange(row.ObservedAtUtc, started.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(row.ObservedAtUtc, binding.GetProperty("observedAtUtc").GetDateTimeOffset());
        Assert.Equal(row.RecordedAtUtc, binding.GetProperty("recordedAtUtc").GetDateTimeOffset());
        Assert.Equal(row.RecordedAtUtc, stored.OccurredAtUtc);
        Assert.Equal(ParticipantKind.Human, stored.ActorKind);
        Assert.Null(stored.AttemptId);
        Assert.Equal(GitHubRepositoryIdentityBindingEventPayload.Build(row), stored.PayloadJson);
        Assert.True(Encoding.UTF8.GetByteCount(stored.PayloadJson) <= GitHubRepositoryIdentityBindingEventPayload.MaximumLength);
    }

    [Fact]
    public async Task The_read_returns_the_same_recorded_graph_and_never_observes_or_writes()
    {
        var delivered = await DeliveredAsync();
        var posted = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));
        var callsAfterBind = delivered.Observer.Calls.Count;
        var before = await DatabaseFingerprintAsync();

        var read = await GetBindingAsync(delivered.Host, delivered.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, read.Status);
        Assert.Equal(GetResponseKeys, KeysOf(read.Root));
        Assert.Equal("Available", read.Root.GetProperty("state").GetString());
        Assert.Equal(posted.Root.GetProperty("source").ToString(), read.Root.GetProperty("source").ToString());
        Assert.Equal(posted.Root.GetProperty("target").ToString(), read.Root.GetProperty("target").ToString());
        Assert.Equal(posted.Root.GetProperty("binding").ToString(), read.Root.GetProperty("binding").ToString());
        Assert.Equal(callsAfterBind, delivered.Observer.Calls.Count);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task Without_a_recorded_target_the_read_is_not_recorded_with_no_scope()
    {
        var withoutTarget = await DeliveredAsync(recordTarget: false);
        var empty = await GetBindingAsync(withoutTarget.Host, withoutTarget.Ids.RunId);
        Assert.Equal("NotRecorded", empty.Root.GetProperty("state").GetString());
        foreach (var member in new[] { "source", "target", "binding" })
        {
            Assert.Equal(JsonValueKind.Null, empty.Root.GetProperty(member).ValueKind);
        }

        var noTarget = await PostBindingAsync(
            withoutTarget.Host, withoutTarget.Ids.RunId, BindingBodyOf(withoutTarget.OperationId, withoutTarget.Sha, Guid.NewGuid()));
        Assert.Equal("github_repository_identity_binding.unavailable", ErrorCode(noTarget));
        Assert.Empty(withoutTarget.Observer.Calls);
    }

    [Fact]
    public async Task A_missing_binding_is_not_recorded_with_the_pair_for_a_coherent_target()
    {
        var withTarget = await DeliveredAsync();
        var open = await GetBindingAsync(withTarget.Host, withTarget.Ids.RunId);
        Assert.Equal(HttpStatusCode.OK, open.Status);
        Assert.Equal("NotRecorded", open.Root.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Object, open.Root.GetProperty("source").ValueKind);
        Assert.Equal(withTarget.TargetId, open.Root.GetProperty("target").GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, open.Root.GetProperty("binding").ValueKind);
        Assert.Empty(withTarget.Observer.Calls);
    }

    [Fact]
    public async Task The_same_binding_replays_the_original_record_event_and_times_without_another_observation_or_insert()
    {
        var delivered = await DeliveredAsync();
        var first = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));
        var row = await BindingRowAsync(delivered.Ids.RunId);
        var fingerprint = await DatabaseFingerprintAsync();

        // Even when the observer would now answer differently or the CLI evidence is gone, a coherent same-identifier binding replays.
        delivered.Observer.Answer = () => Identity(nodeId: "R_changed");
        await using (var db = OpenDb())
        {
            db.HostCapabilitySnapshots.RemoveRange(db.HostCapabilitySnapshots);
            await db.SaveChangesAsync();
        }

        var fingerprintWithoutSnapshot = await DatabaseFingerprintAsync();
        var second = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));

        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(first.Body, second.Body);
        Assert.Single(delivered.Observer.Calls);
        Assert.Equal((1, 1), await BindingCountsAsync(delivered.Ids.RunId));
        Assert.Equal(row.Id, (await BindingRowAsync(delivered.Ids.RunId)).Id);
        Assert.Equal(fingerprintWithoutSnapshot, await DatabaseFingerprintAsync());
        Assert.NotEqual(fingerprint, fingerprintWithoutSnapshot);
    }

    [Fact]
    public async Task A_different_node_identifier_conflicts_with_a_coherent_binding_without_observing_or_changing_it()
    {
        var delivered = await DeliveredAsync();
        await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));
        var original = (await GetBindingAsync(delivered.Host, delivered.Ids.RunId)).Body;
        var fingerprint = await DatabaseFingerprintAsync();

        var conflict = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered, "R_another01"));

        Assert.Equal(HttpStatusCode.Conflict, conflict.Status);
        Assert.Equal("github_repository_identity_binding.conflict", ErrorCode(conflict));
        Assert.DoesNotContain("R_another01", conflict.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(NodeId, conflict.Body, StringComparison.Ordinal);
        Assert.Single(delivered.Observer.Calls);
        Assert.Equal(fingerprint, await DatabaseFingerprintAsync());
        Assert.Equal(original, (await GetBindingAsync(delivered.Host, delivered.Ids.RunId)).Body);
    }

    [Fact]
    public async Task An_unusable_cli_snapshot_is_cli_unavailable_and_nothing_is_invoked_or_written()
    {
        var delivered = await DeliveredAsync(seedSnapshot: false);
        var before = await DatabaseFingerprintAsync();

        var none = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));
        Assert.Equal(HttpStatusCode.Conflict, none.Status);
        Assert.Equal("github_repository_identity_binding.cli_unavailable", ErrorCode(none));

        await SeedSnapshotAsync(CapabilityLaunchKind.NodeScript, @"C:\safe\gh.js");
        Assert.Equal("github_repository_identity_binding.cli_unavailable", ErrorCode(await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered))));
        await SeedSnapshotAsync(failedLast: true);
        Assert.Equal("github_repository_identity_binding.cli_unavailable", ErrorCode(await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered))));
        await SeedSnapshotAsync(evidenceAge: TimeSpan.FromHours(1));
        Assert.Equal("github_repository_identity_binding.cli_unavailable", ErrorCode(await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered))));

        Assert.Empty(delivered.Observer.Calls);
        Assert.Equal((0, 0), await BindingCountsAsync(delivered.Ids.RunId));
    }

    [Fact]
    public async Task A_null_foreign_or_invalid_observation_is_observation_unknown_and_nothing_is_written()
    {
        var delivered = await DeliveredAsync();
        Func<Task<GitHubRepositoryIdentity?>>[] answers =
        [
            () => Task.FromResult<GitHubRepositoryIdentity?>(null),
            () => Identity(owner: "someone-else"),
            () => Identity(repository: "renamed.repo"),
            () => Identity(repository: "hello.world.git"),
            () => Identity(nodeId: ""),
            () => Identity(nodeId: "has space"),
            () => Identity(nodeId: new string('a', 257)),
        ];
        var before = await DatabaseFingerprintAsync();

        foreach (var answer in answers)
        {
            delivered.Observer.Answer = answer;

            var refused = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));

            Assert.Equal(HttpStatusCode.Conflict, refused.Status);
            Assert.Equal("github_repository_identity_binding.observation_unknown", ErrorCode(refused));
            Assert.DoesNotContain("someone-else", refused.Body, StringComparison.Ordinal);
        }

        Assert.Equal(answers.Length, delivered.Observer.Calls.Count);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_valid_observation_of_a_different_node_identifier_is_identity_changed_and_never_echoes_either_value()
    {
        var delivered = await DeliveredAsync();
        delivered.Observer.Answer = () => Identity("OCTO-CAT", "Hello.World", "R_remote_changed");
        var before = await DatabaseFingerprintAsync();

        var refused = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("github_repository_identity_binding.identity_changed", ErrorCode(refused));
        Assert.DoesNotContain("R_remote_changed", refused.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(NodeId, refused.Body, StringComparison.Ordinal);
        Assert.Single(delivered.Observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task Names_that_differ_from_the_target_only_in_ascii_case_are_admitted_and_stored_exactly_as_the_observer_reported_them()
    {
        var delivered = await DeliveredAsync();
        delivered.Observer.Answer = () => Identity("OCTO-CAT", "Hello.World");

        var posted = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));

        Assert.Equal(HttpStatusCode.OK, posted.Status);
        var binding = posted.Root.GetProperty("binding");
        Assert.Equal("OCTO-CAT", binding.GetProperty("owner").GetString());
        Assert.Equal("Hello.World", binding.GetProperty("repository").GetString());
        // The recorded target keeps the names the Human selected.
        Assert.Equal((DefaultOwner, DefaultRepository), (posted.Root.GetProperty("target").GetProperty("owner").GetString(), posted.Root.GetProperty("target").GetProperty("repository").GetString()));
        var row = await BindingRowAsync(delivered.Ids.RunId);
        Assert.Equal(("OCTO-CAT", "Hello.World"), (row.Owner, row.Repository));
        var payload = System.Text.Json.JsonDocument.Parse((await BindingEventRowAsync(delivered.Ids.RunId)).PayloadJson).RootElement;
        Assert.Equal("OCTO-CAT", payload.GetProperty("owner").GetString());
        Assert.Equal("Hello.World", payload.GetProperty("repository").GetString());
        var read = await GetBindingAsync(delivered.Host, delivered.Ids.RunId);
        Assert.Equal("Available", read.Root.GetProperty("state").GetString());
        Assert.Equal("OCTO-CAT", read.Root.GetProperty("binding").GetProperty("owner").GetString());
        Assert.Equal("Hello.World", read.Root.GetProperty("binding").GetProperty("repository").GetString());
        var replay = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));
        Assert.Equal(posted.Body, replay.Body);
        Assert.Single(delivered.Observer.Calls);
    }

    [Fact]
    public async Task A_second_host_clock_reading_earlier_than_the_observation_is_unavailable_and_records_nothing()
    {
        var clock = new ScriptedClock();
        var delivered = await DeliveredAsync(configure: UseClock(clock));
        var now = DateTimeOffset.UtcNow;
        delivered.Observer.Answer = () =>
        {
            clock.Queued.Enqueue(now);
            clock.Queued.Enqueue(now.AddSeconds(-5));
            return Identity();
        };
        var before = await DatabaseFingerprintAsync();

        var refused = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("github_repository_identity_binding.unavailable", ErrorCode(refused));
        Assert.Equal((0, 0), await BindingCountsAsync(delivered.Ids.RunId));
        Assert.Equal(before, await DatabaseFingerprintAsync());
        Assert.Single(delivered.Observer.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Equal_and_ordered_clock_readings_are_recorded_exactly_as_read(int secondsLater)
    {
        var clock = new ScriptedClock();
        var delivered = await DeliveredAsync(configure: UseClock(clock));
        var now = DateTimeOffset.UtcNow;
        delivered.Observer.Answer = () =>
        {
            clock.Queued.Enqueue(now);
            clock.Queued.Enqueue(now.AddSeconds(secondsLater));
            return Identity();
        };

        var posted = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));

        Assert.Equal(HttpStatusCode.OK, posted.Status);
        var row = await BindingRowAsync(delivered.Ids.RunId);
        Assert.Equal(now, row.ObservedAtUtc);
        Assert.Equal(now.AddSeconds(secondsLater), row.RecordedAtUtc);
    }

    [Fact]
    public async Task A_request_that_is_not_the_exact_recorded_scope_is_unavailable_without_observation_or_writes()
    {
        var delivered = await DeliveredAsync();
        var before = await DatabaseFingerprintAsync();
        var bodies = new[]
        {
            BindingBodyOf(Guid.NewGuid(), delivered.Sha, delivered.TargetId),
            BindingBodyOf(delivered.OperationId, Nines40, delivered.TargetId),
            BindingBodyOf(delivered.OperationId, delivered.Sha, Guid.NewGuid()),
        };

        foreach (var body in bodies)
        {
            var refused = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, body);

            Assert.Equal(HttpStatusCode.Conflict, refused.Status);
            Assert.Equal("github_repository_identity_binding.unavailable", ErrorCode(refused));
        }

        Assert.Empty(delivered.Observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task An_unknown_run_is_the_fixed_not_found_for_both_operations()
    {
        var delivered = await DeliveredAsync();
        var unknown = Guid.NewGuid();

        var post = await PostBindingAsync(delivered.Host, unknown, BodyFor(delivered));
        var get = await GetBindingAsync(delivered.Host, unknown);

        foreach (var reading in new[] { post, get })
        {
            Assert.Equal(HttpStatusCode.NotFound, reading.Status);
            Assert.Equal("github_repository_identity_binding.not_found", ErrorCode(reading));
            Assert.Equal("urn:devalente:problem:not-found", reading.Root.GetProperty("type").GetString());
        }

        Assert.Empty(delivered.Observer.Calls);
    }

    [Fact]
    public async Task Both_operations_require_authentication_and_only_get_and_post_are_routed()
    {
        var delivered = await DeliveredAsync();
        using var anonymous = delivered.Host.CreateClient();
        var route = $"/api/runs/{delivered.Ids.RunId}/{BindingRoute}";

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        using var content = new StringContent(BodyFor(delivered), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(route, content)).StatusCode);

        using var client = LocalCommitLineage.AuthenticatedClient(delivered.Host);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Empty(delivered.Observer.Calls);
        Assert.Equal((0, 0), await BindingCountsAsync(delivered.Ids.RunId));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    [InlineData("{\"operationId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public async Task A_malformed_or_incomplete_body_is_the_fixed_invalid_error_that_writes_and_observes_nothing(string body)
    {
        var delivered = await DeliveredAsync();

        var refused = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, body);

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal("github_repository_identity_binding.invalid", ErrorCode(refused));
        Assert.Empty(delivered.Observer.Calls);
        Assert.Equal((0, 0), await BindingCountsAsync(delivered.Ids.RunId));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate-operation")]
    [InlineData("duplicate-node")]
    [InlineData("casing")]
    [InlineData("null-node")]
    [InlineData("number-commit")]
    [InlineData("not-a-guid")]
    [InlineData("braced-guid")]
    [InlineData("object-node")]
    [InlineData("owner")]
    [InlineData("branch")]
    public async Task An_unknown_duplicate_misspelled_or_mistyped_member_is_refused_before_dispatch_without_echo(string kind)
    {
        var delivered = await DeliveredAsync();
        var op = delivered.OperationId;
        var sha = delivered.Sha;
        var target = delivered.TargetId;
        var body = kind switch
        {
            "unknown" => $"{{\"operationId\":\"{op}\",\"commitSha\":\"{sha}\",\"targetId\":\"{target}\",\"repositoryNodeId\":\"{NodeId}\",\"extra\":\"SENTINEL\"}}",
            "duplicate-operation" => $"{{\"operationId\":\"{Guid.NewGuid()}\",\"operationId\":\"{op}\",\"commitSha\":\"{sha}\",\"targetId\":\"{target}\",\"repositoryNodeId\":\"{NodeId}\"}}",
            "duplicate-node" => $"{{\"operationId\":\"{op}\",\"commitSha\":\"{sha}\",\"targetId\":\"{target}\",\"repositoryNodeId\":\"SENTINEL\",\"repositoryNodeId\":\"{NodeId}\"}}",
            "casing" => $"{{\"OperationId\":\"{op}\",\"commitSha\":\"{sha}\",\"targetId\":\"{target}\",\"repositoryNodeId\":\"{NodeId}\"}}",
            "null-node" => $"{{\"operationId\":\"{op}\",\"commitSha\":\"{sha}\",\"targetId\":\"{target}\",\"repositoryNodeId\":null}}",
            "number-commit" => $"{{\"operationId\":\"{op}\",\"commitSha\":12345,\"targetId\":\"{target}\",\"repositoryNodeId\":\"{NodeId}\"}}",
            "not-a-guid" => $"{{\"operationId\":\"SENTINEL\",\"commitSha\":\"{sha}\",\"targetId\":\"{target}\",\"repositoryNodeId\":\"{NodeId}\"}}",
            "braced-guid" => $"{{\"operationId\":\"{{{op}}}\",\"commitSha\":\"{sha}\",\"targetId\":\"{target}\",\"repositoryNodeId\":\"{NodeId}\"}}",
            "object-node" => $"{{\"operationId\":\"{op}\",\"commitSha\":\"{sha}\",\"targetId\":\"{target}\",\"repositoryNodeId\":{{\"SENTINEL\":1}}}}",
            "owner" => $"{{\"operationId\":\"{op}\",\"commitSha\":\"{sha}\",\"targetId\":\"{target}\",\"repositoryNodeId\":\"{NodeId}\",\"owner\":\"evil-owner\"}}",
            _ => $"{{\"operationId\":\"{op}\",\"commitSha\":\"{sha}\",\"targetId\":\"{target}\",\"repositoryNodeId\":\"{NodeId}\",\"branch\":\"evil\"}}",
        };

        var refused = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, body);

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal("github_repository_identity_binding.invalid", ErrorCode(refused));
        Assert.DoesNotContain("SENTINEL", refused.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("evil", refused.Body, StringComparison.Ordinal);
        Assert.Empty(delivered.Observer.Calls);
        Assert.Equal((0, 0), await BindingCountsAsync(delivered.Ids.RunId));
    }

    [Theory]
    [InlineData("commit-upper")]
    [InlineData("commit-short")]
    [InlineData("commit-long")]
    [InlineData("node-empty")]
    [InlineData("node-space")]
    [InlineData("node-dot")]
    [InlineData("node-nonascii")]
    [InlineData("node-257")]
    [InlineData("operation-empty")]
    [InlineData("target-empty")]
    public async Task An_invalid_value_is_the_fixed_bad_request_that_is_never_echoed_and_writes_and_observes_nothing(string kind)
    {
        var delivered = await DeliveredAsync();
        var (operation, sha, target, node) = (delivered.OperationId, delivered.Sha, delivered.TargetId, NodeId);
        (operation, sha, target, node) = kind switch
        {
            "commit-upper" => (operation, sha.ToUpperInvariant() + "SECRET", target, node),
            "commit-short" => (operation, sha[..39], target, node),
            "commit-long" => (operation, sha + "a", target, node),
            "node-empty" => (operation, sha, target, string.Empty),
            "node-space" => (operation, sha, target, "SECRET value"),
            "node-dot" => (operation, sha, target, "SECRET.value"),
            "node-nonascii" => (operation, sha, target, "SECRET-caf" + (char)0x00e9),
            "node-257" => (operation, sha, target, new string('a', 257)),
            "operation-empty" => (Guid.Empty, sha, target, node),
            _ => (operation, sha, Guid.Empty, node),
        };

        var refused = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BindingBodyOf(operation, sha, target, node));

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal("github_repository_identity_binding.invalid", ErrorCode(refused));
        Assert.DoesNotContain("SECRET", refused.Body, StringComparison.Ordinal);
        Assert.Empty(delivered.Observer.Calls);
        Assert.Equal((0, 0), await BindingCountsAsync(delivered.Ids.RunId));
    }

    [Fact]
    public async Task The_longest_supported_node_identifier_is_bound_and_replayed_within_the_event_bound()
    {
        var delivered = await DeliveredAsync();
        var longest = new string('a', 128) + new string('+', 128);
        delivered.Observer.Answer = () => Identity(nodeId: longest);

        var posted = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered, longest));
        var replay = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered, longest));

        Assert.Equal(HttpStatusCode.OK, posted.Status);
        Assert.Equal(posted.Body, replay.Body);
        var stored = await BindingEventRowAsync(delivered.Ids.RunId);
        Assert.True(Encoding.UTF8.GetByteCount(stored.PayloadJson) <= GitHubRepositoryIdentityBindingEventPayload.MaximumLength);
        Assert.Equal("Available", (await GetBindingAsync(delivered.Host, delivered.Ids.RunId)).Root.GetProperty("state").GetString());
    }

    [Fact]
    public async Task The_observation_runs_with_no_lock_held_so_an_independent_writer_proceeds_while_the_observer_waits()
    {
        var delivered = await DeliveredAsync();
        var observerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var independentWriteSucceeded = false;
        delivered.Observer.Answer = async () =>
        {
            observerStarted.SetResult();

            // A genuine write on an independent connection with a short busy timeout: it fails if any write lock is held.
            await ExecuteSqlAsync("UPDATE runs SET \"Lifecycle\" = \"Lifecycle\"", busyTimeoutSeconds: 2);
            independentWriteSucceeded = true;
            await release.Task;
            return new GitHubRepositoryIdentity(NodeId, DefaultOwner, DefaultRepository);
        };

        var request = PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));
        await observerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(300);
        Assert.False(request.IsCompleted);
        release.SetResult();
        var posted = await request;

        Assert.True(independentWriteSucceeded);
        Assert.Equal(HttpStatusCode.OK, posted.Status);
    }

    [Fact]
    public async Task Concurrent_requests_create_one_binding_and_one_event_and_return_the_same_record()
    {
        var delivered = await DeliveredAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        delivered.Observer.Answer = async () =>
        {
            await gate.Task;
            return new GitHubRepositoryIdentity(NodeId, DefaultOwner, DefaultRepository);
        };

        var requests = Enumerable.Range(0, 4)
            .Select(_ => PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered)))
            .ToArray();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (delivered.Observer.Calls.Count < 4 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        gate.SetResult();
        var results = await Task.WhenAll(requests);

        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.Status));
        Assert.Single(results.Select(result => result.Body).Distinct());
        Assert.Equal((1, 1), await BindingCountsAsync(delivered.Ids.RunId));
    }

    [Fact]
    public async Task A_scope_lost_or_changed_during_the_observation_refuses_the_binding_and_records_nothing()
    {
        var delivered = await DeliveredAsync();
        var eventOfTarget = $"\"EventType\" = '{TargetEventType}' AND \"RunId\" = '{Upper(delivered.Ids.RunId)}'";
        var targetId = Upper(delivered.TargetId);
        (string[] Tables, string Statement)[] cases =
        [
            (["events"], $"DELETE FROM events WHERE {eventOfTarget}"),
            (["github_observation_targets"], $"UPDATE github_observation_targets SET \"Owner\" = 'someone-else' WHERE \"Id\" = '{targetId}'"),
            (["github_observation_targets"], $"DELETE FROM github_observation_targets WHERE \"Id\" = '{targetId}'"),
            (["events"], $"UPDATE events SET \"PayloadJson\" = '' WHERE {eventOfTarget}"),
            (["local_commit_operations"], $"UPDATE local_commit_operations SET \"BranchName\" = 'elsewhere' WHERE \"Id\" = '{Upper(delivered.OperationId)}'"),
        ];

        foreach (var (tables, statement) in cases)
        {
            await CorruptAsync(tables, [], async () =>
            {
                delivered.Observer.Answer = async () =>
                {
                    await ExecuteSqlAsync(statement);
                    return new GitHubRepositoryIdentity(NodeId, DefaultOwner, DefaultRepository);
                };

                var refused = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));

                Assert.True(refused.Status == HttpStatusCode.Conflict, $"{statement}: {refused.Body}");
                Assert.True(ErrorCode(refused) == "github_repository_identity_binding.unavailable", $"{statement}: {refused.Body}");
                Assert.True((await BindingCountsAsync(delivered.Ids.RunId)) == (0, 0), statement);
            });
        }

        Assert.Equal(cases.Length, delivered.Observer.Calls.Count);
    }

    [Fact]
    public async Task An_unexpected_observer_fault_is_not_converted_into_a_status_and_writes_nothing()
    {
        var delivered = await DeliveredAsync();
        delivered.Observer.Answer = () => throw new InvalidOperationException("boom");
        var before = await DatabaseFingerprintAsync();

        var reading = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));

        Assert.Equal(HttpStatusCode.InternalServerError, reading.Status);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_request_body_on_the_read_is_refused_with_a_fixed_error_for_every_form_before_anything_is_dispatched()
    {
        var delivered = await DeliveredAsync();
        await PostBindingAsync(delivered.Host, delivered.Ids.RunId, BodyFor(delivered));
        var before = await DatabaseFingerprintAsync();
        const string Sentinel = "sentinel-owner-evil";

        var declared = await GetBindingAsync(
            delivered.Host, delivered.Ids.RunId, new StringContent($"{{\"owner\":\"{Sentinel}\"}}", Encoding.UTF8, "application/json"));
        var seekable = await GetBindingAsync(delivered.Host, delivered.Ids.RunId, new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("{}"))));
        using var undeclared = new StreamContent(new UnsizedStream(Encoding.UTF8.GetBytes("{}")));
        var unsized = await GetBindingAsync(delivered.Host, delivered.Ids.RunId, undeclared);
        var unknownRunWithBody = await GetBindingAsync(delivered.Host, Guid.NewGuid(), new StringContent("{}"));

        foreach (var refused in new[] { declared, seekable, unsized, unknownRunWithBody })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal("github_repository_identity_binding.body_not_allowed", ErrorCode(refused));
            Assert.DoesNotContain(Sentinel, refused.Body, StringComparison.Ordinal);
        }

        Assert.Equal(before, await DatabaseFingerprintAsync());

        // Query strings are ignored and an empty body is not a body.
        using var client = LocalCommitLineage.AuthenticatedClient(delivered.Host);
        var withQuery = await client.GetAsync($"/api/runs/{delivered.Ids.RunId}/{BindingRoute}?owner={Sentinel}");
        Assert.Equal(HttpStatusCode.OK, withQuery.StatusCode);
    }

    [Fact]
    public async Task An_empty_chunked_request_with_no_declared_length_and_zero_bytes_is_refused()
    {
        var delivered = await DeliveredAsync();
        var before = await DatabaseFingerprintAsync();
        using var client = LocalCommitLineage.AuthenticatedClient(delivered.Host);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{delivered.Ids.RunId}/{BindingRoute}")
        {
            Content = new StreamContent(new UnsizedStream([])),
        };
        request.Headers.TransferEncodingChunked = true;

        var response = await client.SendAsync(request);
        var reading = new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());

        Assert.Null(request.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.BadRequest, reading.Status);
        Assert.Equal("github_repository_identity_binding.body_not_allowed", ErrorCode(reading));
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
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
        var body = BindingBodyOf(Guid.NewGuid(), new string('a', 40), Guid.NewGuid());

        using var oversized = new StringContent(body[..^1] + ",\"padding\":\"" + new string('x', 9 * 1024) + "\"}", Encoding.UTF8, "application/json");
        using var inside = new StringContent(body, Encoding.UTF8, "application/json");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync($"/api/runs/{run}/{BindingRoute}", oversized)).StatusCode);
        var accepted = await client.PostAsync($"/api/runs/{run}/{BindingRoute}", inside);
        Assert.Equal(HttpStatusCode.NotFound, accepted.StatusCode);
        Assert.Contains("github_repository_identity_binding.not_found", await accepted.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
