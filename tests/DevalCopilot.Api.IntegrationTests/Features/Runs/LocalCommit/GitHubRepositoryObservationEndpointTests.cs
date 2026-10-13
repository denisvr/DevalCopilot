using System.Net;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryObservation;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0041 through the production host: a real delivered run and its recorded ADR-0040 target, a scripted provider-neutral observer,
/// and the real MVC authentication, mediator, readers and file-backed SQLite. Every read is proved to write nothing, and the observer
/// is proved to be invoked exactly when (and only with what) the recorded scope and the current CLI snapshot admit.
/// </summary>
public sealed class GitHubRepositoryObservationEndpointTests : GitHubObservationTargetTestBase
{
    private const string ObservationRoute = "github-repository-observation";
    private const string GhPath = @"C:\safe\gh.exe";
    private const string NodeId = "R_kgDOExample01";

    private static readonly string[] ResponseKeys = ["observation", "runId", "source", "status", "target"];

    private sealed class ScriptedObserver : IGitHubRepositoryIdentityObserver
    {
        public List<(string Path, string Owner, string Repository)> Calls { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public DateTimeOffset? LastStartedAt { get; private set; }

        public Func<Task<GitHubRepositoryIdentity?>> Answer { get; set; } = () => Task.FromResult<GitHubRepositoryIdentity?>(null);

        public Task<GitHubRepositoryIdentity?> ObserveAsync(
            string executablePath, string owner, string repository, CancellationToken cancellationToken)
        {
            Calls.Add((executablePath, owner, repository));
            Tokens.Add(cancellationToken);
            LastStartedAt = DateTimeOffset.UtcNow;
            return Answer();
        }
    }

    private static Task<GitHubRepositoryIdentity?> Identity(string owner = DefaultOwner, string repository = DefaultRepository, string nodeId = NodeId) =>
        Task.FromResult<GitHubRepositoryIdentity?>(new GitHubRepositoryIdentity(nodeId, owner, repository));

    private async Task SeedSnapshotAsync(
        CapabilityLaunchKind kind = CapabilityLaunchKind.DirectExecutable, string? script = null, bool failedLast = false,
        TimeSpan? evidenceAge = null)
    {
        await using var db = OpenDb();
        var now = DateTimeOffset.UtcNow - (evidenceAge ?? TimeSpan.Zero);
        var snapshot = await db.HostCapabilitySnapshots.SingleOrDefaultAsync(candidate => candidate.Capability == Capability.GitHubCli);
        if (snapshot is null)
        {
            snapshot = HostCapabilitySnapshot.Seed(Capability.GitHubCli, now);
            db.HostCapabilitySnapshots.Add(snapshot);
        }

        snapshot.MarkDispatched(now);
        snapshot.RecordSuccess(kind, GhPath, script, "2.50.0", now, now.AddMinutes(5));
        if (failedLast)
        {
            snapshot.MarkDispatched(now);
            snapshot.RecordFailure(CapabilityProbeReason.ExecutableInaccessible, now.AddMinutes(5));
        }

        await db.SaveChangesAsync();
    }

    private async Task<(LocalCommitHost Host, ScriptedObserver Observer, LocalCommitLineageIds Ids, Guid OperationId, string Sha)> RecordedHostAsync(
        bool recordTarget = true)
    {
        LocalCommitLineageIds ids;
        Guid operationId;
        using (var delivering = StartHost(runSupervisor: true))
        {
            (ids, operationId) = await DeliverAsync(delivering);
        }

        var observer = new ScriptedObserver();
        var host = StartHost(
            runSupervisor: false,
            configure: services =>
            {
                services.RemoveAll<IGitHubRepositoryIdentityObserver>();
                services.AddSingleton<IGitHubRepositoryIdentityObserver>(observer);
            });
        var sha = await CommitShaOfAsync(operationId);
        if (recordTarget)
        {
            await RecordAsync(host, ids.RunId, operationId, sha);
        }

        return (host, observer, ids, operationId, sha);
    }

    private static async Task<ReceiptReading> ObserveAsync(LocalCommitHost host, Guid runId, HttpContent? body = null, bool authenticated = true)
    {
        using var client = authenticated ? LocalCommitLineage.AuthenticatedClient(host) : host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{runId}/{ObservationRoute}") { Content = body };
        var response = await client.SendAsync(request);
        return new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string ErrorCode(ReceiptReading reading) => reading.Root.GetProperty("errors")[0].GetProperty("code").GetString()!;

    private static string StatusOf(ReceiptReading reading) => reading.Root.GetProperty("status").GetString()!;

    [Fact]
    public async Task The_route_is_protected_and_only_get_is_routed()
    {
        var (host, observer, ids, _, _) = await RecordedHostAsync();
        await SeedSnapshotAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await ObserveAsync(host, ids.RunId, authenticated: false)).Status);
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var route = $"/api/runs/{ids.RunId}/{ObservationRoute}";
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(route, new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(route)).StatusCode);
        Assert.Empty(observer.Calls);
    }

    [Fact]
    public async Task An_unknown_run_is_the_fixed_404_and_nothing_is_invoked()
    {
        var (host, observer, _, _, _) = await RecordedHostAsync(recordTarget: false);
        await SeedSnapshotAsync();

        var unknown = await ObserveAsync(host, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.Equal("github_repository_observation.not_found", ErrorCode(unknown));
        Assert.Equal("urn:devalente:problem:not-found", unknown.Root.GetProperty("type").GetString());
        Assert.Empty(observer.Calls);
    }

    [Fact]
    public async Task A_delivery_without_a_recorded_target_is_not_recorded_with_no_scope_and_nothing_is_invoked_or_written()
    {
        var (host, observer, ids, _, _) = await RecordedHostAsync(recordTarget: false);
        await SeedSnapshotAsync();
        var before = await DatabaseFingerprintAsync();

        var reading = await ObserveAsync(host, ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal(ResponseKeys, KeysOf(reading.Root));
        Assert.Equal("NotRecorded", StatusOf(reading));
        Assert.Equal(ids.RunId, reading.Root.GetProperty("runId").GetGuid());
        foreach (var scope in new[] { "source", "target", "observation" })
        {
            Assert.Equal(JsonValueKind.Null, reading.Root.GetProperty(scope).ValueKind);
        }

        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_corrupt_recorded_scope_is_unavailable_with_no_scope_and_nothing_is_invoked()
    {
        var (host, observer, ids, operationId, _) = await RecordedHostAsync();
        await SeedSnapshotAsync();
        var row = await TargetRowAsync(ids.RunId);
        var eventOfTarget = $"\"EventType\" = '{TargetEventType}' AND \"RunId\" = '{Upper(ids.RunId)}'";
        (string[] Tables, string Statement)[] cases =
        [
            (["github_observation_targets"], $"UPDATE github_observation_targets SET \"Owner\" = 'bad owner' WHERE \"Id\" = '{Upper(row.Id)}'"),
            (["github_observation_targets"], $"UPDATE github_observation_targets SET \"Owner\" = 'someone-else' WHERE \"Id\" = '{Upper(row.Id)}'"),
            (["events"], $"DELETE FROM events WHERE {eventOfTarget}"),
            (["events"], $"UPDATE events SET \"PayloadJson\" = '' WHERE {eventOfTarget}"),
            (["local_commit_operations"], $"UPDATE local_commit_operations SET \"BranchName\" = 'elsewhere' WHERE \"Id\" = '{Upper(operationId)}'"),
        ];

        foreach (var (tables, statement) in cases)
        {
            await CorruptAsync(tables, [statement], async () =>
            {
                var reading = await ObserveAsync(host, ids.RunId);

                Assert.True(reading.Status == HttpStatusCode.OK, statement);
                Assert.True(StatusOf(reading) == "Unavailable", statement);
                Assert.True(reading.Root.GetProperty("source").ValueKind == JsonValueKind.Null, statement);
                Assert.True(reading.Root.GetProperty("target").ValueKind == JsonValueKind.Null, statement);
                Assert.True(reading.Root.GetProperty("observation").ValueKind == JsonValueKind.Null, statement);
                await Task.CompletedTask;
            });
        }

        Assert.Empty(observer.Calls);
    }

    [Fact]
    public async Task An_unusable_cli_snapshot_is_cli_unavailable_with_the_exact_scope_and_nothing_is_invoked()
    {
        var (host, observer, ids, operationId, sha) = await RecordedHostAsync();
        var row = await TargetRowAsync(ids.RunId);

        // No snapshot evidence at all.
        var none = await ObserveAsync(host, ids.RunId);
        Assert.Equal("CliUnavailable", StatusOf(none));
        Assert.Equal(operationId, none.Root.GetProperty("source").GetProperty("operationId").GetGuid());
        Assert.Equal(sha, none.Root.GetProperty("source").GetProperty("commitSha").GetString());
        Assert.Equal(row.Id, none.Root.GetProperty("target").GetProperty("id").GetGuid());
        Assert.Equal(DefaultOwner, none.Root.GetProperty("target").GetProperty("owner").GetString());
        Assert.Equal(JsonValueKind.Null, none.Root.GetProperty("observation").ValueKind);

        await SeedSnapshotAsync(CapabilityLaunchKind.NodeScript, @"C:\safe\gh.js");
        Assert.Equal("CliUnavailable", StatusOf(await ObserveAsync(host, ids.RunId)));

        await SeedSnapshotAsync(failedLast: true);
        Assert.Equal("CliUnavailable", StatusOf(await ObserveAsync(host, ids.RunId)));

        // Evidence older than its due time plus the two-minute grace is stale, however successful it was.
        await SeedSnapshotAsync(evidenceAge: TimeSpan.FromHours(1));
        Assert.Equal("CliUnavailable", StatusOf(await ObserveAsync(host, ids.RunId)));

        Assert.Empty(observer.Calls);
    }

    [Fact]
    public async Task An_observed_identity_is_returned_exactly_with_the_scope_the_vetted_path_the_stored_names_and_no_write()
    {
        var (host, observer, ids, operationId, sha) = await RecordedHostAsync();
        await SeedSnapshotAsync();
        observer.Answer = () => Identity("OCTO-CAT", "Hello.World");
        var before = await DatabaseFingerprintAsync();
        var started = DateTimeOffset.UtcNow;

        var reading = await ObserveAsync(host, ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal(ResponseKeys, KeysOf(reading.Root));
        Assert.Equal("Observed", StatusOf(reading));
        var call = Assert.Single(observer.Calls);
        Assert.Equal((GhPath, DefaultOwner, DefaultRepository), call);
        Assert.Equal(["branchName", "commitSha", "operationId"], KeysOf(reading.Root.GetProperty("source")));
        Assert.Equal(operationId, reading.Root.GetProperty("source").GetProperty("operationId").GetGuid());
        Assert.Equal(sha, reading.Root.GetProperty("source").GetProperty("commitSha").GetString());
        Assert.Equal(Scene.BranchName, reading.Root.GetProperty("source").GetProperty("branchName").GetString());
        var row = await TargetRowAsync(ids.RunId);
        var target = reading.Root.GetProperty("target");
        Assert.Equal(["eventId", "host", "id", "owner", "recordedAtUtc", "repository"], KeysOf(target));
        Assert.Equal(row.Id, target.GetProperty("id").GetGuid());
        Assert.Equal(row.EventId, target.GetProperty("eventId").GetGuid());
        Assert.Equal("github.com", target.GetProperty("host").GetString());
        Assert.Equal(DefaultOwner, target.GetProperty("owner").GetString());
        Assert.Equal(DefaultRepository, target.GetProperty("repository").GetString());
        var observation = reading.Root.GetProperty("observation");
        Assert.Equal(["observedAtUtc", "owner", "repository", "repositoryNodeId"], KeysOf(observation));
        Assert.Equal(NodeId, observation.GetProperty("repositoryNodeId").GetString());
        Assert.Equal("OCTO-CAT", observation.GetProperty("owner").GetString());
        Assert.Equal("Hello.World", observation.GetProperty("repository").GetString());
        var observedAt = observation.GetProperty("observedAtUtc").GetDateTimeOffset();
        Assert.InRange(observedAt, started.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.DoesNotContain(GhPath, reading.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await DatabaseFingerprintAsync());
        Assert.Equal((1, 1), await CountsAsync(ids.RunId));
    }

    [Fact]
    public async Task The_observation_time_is_stamped_after_the_external_work_and_the_callers_cancellation_reaches_the_observer()
    {
        var (host, observer, ids, _, _) = await RecordedHostAsync();
        await SeedSnapshotAsync();
        observer.Answer = async () =>
        {
            await Task.Delay(1_200);
            return new GitHubRepositoryIdentity(NodeId, DefaultOwner, DefaultRepository);
        };

        var reading = await ObserveAsync(host, ids.RunId);

        Assert.Equal("Observed", StatusOf(reading));
        var observedAt = reading.Root.GetProperty("observation").GetProperty("observedAtUtc").GetDateTimeOffset();
        Assert.True(observedAt >= observer.LastStartedAt!.Value.AddMilliseconds(1_100), $"{observedAt:o} vs {observer.LastStartedAt:o}");
        Assert.True(Assert.Single(observer.Tokens).CanBeCanceled);
    }

    [Fact]
    public async Task An_unusable_or_foreign_answer_is_unknown_with_the_scope_and_no_observation()
    {
        var (host, observer, ids, _, _) = await RecordedHostAsync();
        await SeedSnapshotAsync();
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

        foreach (var answer in answers)
        {
            observer.Answer = answer;

            var reading = await ObserveAsync(host, ids.RunId);

            Assert.Equal(HttpStatusCode.OK, reading.Status);
            Assert.Equal("Unknown", StatusOf(reading));
            Assert.Equal(JsonValueKind.Object, reading.Root.GetProperty("source").ValueKind);
            Assert.Equal(JsonValueKind.Object, reading.Root.GetProperty("target").ValueKind);
            Assert.Equal(JsonValueKind.Null, reading.Root.GetProperty("observation").ValueKind);
        }

        Assert.Equal(answers.Length, observer.Calls.Count);
    }

    [Fact]
    public async Task A_scope_lost_or_changed_during_the_observation_refuses_the_answer_and_returns_no_remote_data()
    {
        var (host, observer, ids, _, _) = await RecordedHostAsync();
        await SeedSnapshotAsync();
        var eventOfTarget = $"\"EventType\" = '{TargetEventType}' AND \"RunId\" = '{Upper(ids.RunId)}'";
        var row = await TargetRowAsync(ids.RunId);
        (string[] Tables, string Statement)[] cases =
        [
            (["events"], $"DELETE FROM events WHERE {eventOfTarget}"),
            (["github_observation_targets"], $"UPDATE github_observation_targets SET \"Owner\" = 'someone-else' WHERE \"Id\" = '{Upper(row.Id)}'"),
            (["github_observation_targets"], $"DELETE FROM github_observation_targets WHERE \"Id\" = '{Upper(row.Id)}'"),
            (["events"], $"UPDATE events SET \"PayloadJson\" = '' WHERE {eventOfTarget}"),
        ];

        foreach (var (tables, statement) in cases)
        {
            await CorruptAsync(tables, [], async () =>
            {
                observer.Answer = async () =>
                {
                    await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DatabasePath}");
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and identifiers this test generated.
                    command.CommandText = statement;
#pragma warning restore CA2100
                    await command.ExecuteNonQueryAsync();
                    return new GitHubRepositoryIdentity(NodeId, DefaultOwner, DefaultRepository);
                };

                var reading = await ObserveAsync(host, ids.RunId);

                Assert.True(reading.Status == HttpStatusCode.OK, statement);
                Assert.True(StatusOf(reading) == "Unavailable", $"{statement}: {reading.Body}");
                Assert.True(reading.Root.GetProperty("source").ValueKind == JsonValueKind.Null, statement);
                Assert.True(reading.Root.GetProperty("target").ValueKind == JsonValueKind.Null, statement);
                Assert.True(reading.Root.GetProperty("observation").ValueKind == JsonValueKind.Null, statement);
                Assert.DoesNotContain(NodeId, reading.Body, StringComparison.Ordinal);
            });
        }

        Assert.Equal(cases.Length, observer.Calls.Count);
    }

    [Fact]
    public async Task A_request_body_is_refused_with_a_fixed_error_before_anything_is_observed_or_written()
    {
        var (host, observer, ids, _, _) = await RecordedHostAsync();
        await SeedSnapshotAsync();
        observer.Answer = () => Identity();
        var before = await DatabaseFingerprintAsync();
        const string Sentinel = "sentinel-owner-evil";

        var declared = await ObserveAsync(
            host, ids.RunId, new StringContent($"{{\"owner\":\"{Sentinel}\",\"url\":\"https://evil.example/x/y\"}}", Encoding.UTF8, "application/json"));
        var chunked = await ObserveAsync(host, ids.RunId, new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("{}")) { Position = 0 }));
        using var undeclared = new StreamContent(new UnsizedStream(Encoding.UTF8.GetBytes("{}")));
        var unsized = await ObserveAsync(host, ids.RunId, undeclared);
        var unknownRunWithBody = await ObserveAsync(host, Guid.NewGuid(), new StringContent("{}"));

        foreach (var refused in new[] { declared, chunked, unsized, unknownRunWithBody })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal("github_repository_observation.body_not_allowed", ErrorCode(refused));
            Assert.DoesNotContain(Sentinel, refused.Body, StringComparison.Ordinal);
        }

        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());

        // Query strings are ignored and an empty body is not a body.
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var withQuery = await client.GetAsync($"/api/runs/{ids.RunId}/{ObservationRoute}?owner={Sentinel}&host=evil.example");
        Assert.Equal(HttpStatusCode.OK, withQuery.StatusCode);
        Assert.Equal((GhPath, DefaultOwner, DefaultRepository), Assert.Single(observer.Calls));
    }

    [Fact]
    public async Task An_unexpected_observer_fault_is_not_converted_into_a_status_and_writes_nothing()
    {
        var (host, observer, ids, _, _) = await RecordedHostAsync();
        await SeedSnapshotAsync();
        observer.Answer = () => throw new InvalidOperationException("boom");
        var before = await DatabaseFingerprintAsync();

        var reading = await ObserveAsync(host, ids.RunId);

        Assert.Equal(HttpStatusCode.InternalServerError, reading.Status);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Theory]
    [InlineData("ResolvedExecutablePath", "'gh.exe'")]
    [InlineData("ResolvedExecutablePath", "'.\\gh.exe'")]
    [InlineData("ObservedVersion", "NULL")]
    [InlineData("ObservedVersion", "''")]
    [InlineData("ObservedVersion", "' '")]
    public async Task A_stored_snapshot_outside_what_domain_construction_allows_is_cli_unavailable_without_invocation_or_write(
        string column, string value)
    {
        var (host, observer, ids, _, _) = await RecordedHostAsync();
        await SeedSnapshotAsync();
        observer.Answer = () => Identity();

        // Direct stored edits are not constrained by Domain construction, so the handler itself must refuse them.
        await CorruptAsync(
            ["host_capability_snapshots"],
            [$"UPDATE host_capability_snapshots SET \"{column}\" = {value} WHERE \"Capability\" = 'GitHubCli'"],
            async () =>
            {
                var before = await DatabaseFingerprintAsync();

                var reading = await ObserveAsync(host, ids.RunId);

                Assert.True(reading.Status == HttpStatusCode.OK, $"{column} {value}");
                Assert.True(StatusOf(reading) == "CliUnavailable", $"{column} {value}: {reading.Body}");
                Assert.True(reading.Root.GetProperty("observation").ValueKind == JsonValueKind.Null, $"{column} {value}");
                Assert.True(observer.Calls.Count == 0, $"{column} {value}");
                Assert.True(before == await DatabaseFingerprintAsync(), $"{column} {value}");
            });
    }

    [Fact]
    public async Task An_empty_chunked_request_with_no_declared_length_and_zero_bytes_is_refused_before_observation()
    {
        var (host, observer, ids, _, _) = await RecordedHostAsync();
        await SeedSnapshotAsync();
        observer.Answer = () => Identity();
        var before = await DatabaseFingerprintAsync();
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{ids.RunId}/{ObservationRoute}")
        {
            Content = new StreamContent(new UnsizedStream([])),
        };
        request.Headers.TransferEncodingChunked = true;

        var response = await client.SendAsync(request);
        var reading = new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());

        Assert.Null(request.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.BadRequest, reading.Status);
        Assert.Equal("github_repository_observation.body_not_allowed", ErrorCode(reading));
        Assert.Empty(observer.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    /// <summary>A readable stream with no known length, so the client sends it chunked rather than with a declared length.</summary>
    private sealed class UnsizedStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
