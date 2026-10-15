using System.Net;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0049 through the production host: a real delivered run (the unchanged production lineage: delivery, supervisor-driven local commit,
/// target POST and binding POST), a scripted provider-neutral permission observer, and the real MVC authentication, mediator, readers and
/// file-backed SQLite. Every read is proved to write nothing, to reuse nothing from an earlier answer or a sibling observation, and to
/// invoke the observer exactly when (and only with what) the recorded scope, the binding and the current CLI snapshot admit.
/// </summary>
public sealed class GitHubRepositoryPermissionObservationEndpointTests : GitHubRepositoryPermissionObservationTestBase
{
    private async Task AssertScopeAsync(ReceiptReading reading, Bound bound, bool binding, string context = "")
    {
        var source = reading.Root.GetProperty("source");
        Assert.Equal(["branchName", "commitSha", "operationId"], KeysOf(source));
        Assert.Equal(bound.OperationId, source.GetProperty("operationId").GetGuid());
        Assert.Equal(bound.Sha, source.GetProperty("commitSha").GetString());
        Assert.Equal(Scene.BranchName, source.GetProperty("branchName").GetString());
        var row = await TargetRowAsync(bound.Ids.RunId);
        var target = reading.Root.GetProperty("target");
        Assert.Equal(["eventId", "host", "id", "owner", "recordedAtUtc", "repository"], KeysOf(target));
        Assert.Equal(row.Id, target.GetProperty("id").GetGuid());
        Assert.Equal(row.EventId, target.GetProperty("eventId").GetGuid());
        Assert.Equal("github.com", target.GetProperty("host").GetString());
        Assert.Equal(DefaultOwner, target.GetProperty("owner").GetString());
        Assert.Equal(DefaultRepository, target.GetProperty("repository").GetString());
        if (!binding)
        {
            Assert.True(reading.Root.GetProperty("binding").ValueKind == JsonValueKind.Null, context);
            return;
        }

        var stored = await BindingRowAsync(bound.Ids.RunId);
        var recorded = reading.Root.GetProperty("binding");
        Assert.Equal(BindingKeys, KeysOf(recorded));
        Assert.Equal(stored.Id, recorded.GetProperty("id").GetGuid());
        Assert.Equal(stored.EventId, recorded.GetProperty("eventId").GetGuid());
        Assert.Equal(NodeId, recorded.GetProperty("repositoryNodeId").GetString());
        Assert.Equal(DefaultOwner, recorded.GetProperty("owner").GetString());
        Assert.Equal(DefaultRepository, recorded.GetProperty("repository").GetString());
        Assert.Equal(stored.ObservedAtUtc, recorded.GetProperty("observedAtUtc").GetDateTimeOffset());
        Assert.Equal(stored.RecordedAtUtc, recorded.GetProperty("recordedAtUtc").GetDateTimeOffset());
    }

    private void AssertCall(Bound bound, int expectedCalls)
    {
        Assert.Equal(expectedCalls, bound.Permission.Calls.Count);
        Assert.Equal((GhPath, DefaultOwner, DefaultRepository), bound.Permission.Calls[^1]);
    }

    private async Task AssertObservationAsync(ReceiptReading reading, Bound bound, DateTimeOffset startedAt, string? permission, string context)
    {
        var observation = reading.Root.GetProperty("observation");
        Assert.Equal(ObservationKeys, KeysOf(observation));
        Assert.Equal(NodeId, observation.GetProperty("repositoryNodeId").GetString());
        Assert.Equal("OCTO-CAT", observation.GetProperty("owner").GetString());
        Assert.Equal("Hello.World", observation.GetProperty("repository").GetString());
        var observedAt = observation.GetProperty("observedAtUtc").GetDateTimeOffset();
        Assert.InRange(observedAt, startedAt.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.True(observedAt >= await BindingRecordedAtAsync(bound.Ids.RunId), context);
        var role = observation.GetProperty("viewerPermission");
        if (permission is null)
        {
            Assert.True(role.ValueKind == JsonValueKind.Null, $"{context}: {reading.Body}");
        }
        else
        {
            Assert.Equal(JsonValueKind.String, role.ValueKind);
            Assert.Equal(permission, role.GetString());
        }
    }

    private static void AssertNoDerivedWords(ReceiptReading reading)
    {
        foreach (var word in new[] { "canPush", "allowed", "Allowed", "denied", "Denied", "ready", "Ready", "eligible", "granted", "rank" })
        {
            Assert.DoesNotContain(word, reading.Body, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(GhPath, reading.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_complete_observation_runs_on_a_freshly_delivered_production_lineage_and_writes_nothing()
    {
        var bound = await BoundAsync();
        bound.Permission.Answer = () => Matching("MAINTAIN");
        var before = await DatabaseFingerprintAsync();
        var started = DateTimeOffset.UtcNow;

        var receipt = await ReceiptAsync(bound.Host, bound.Ids.RunId);
        var reading = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal("Available", receipt.State);
        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal(PermissionResponseKeys, KeysOf(reading.Root));
        Assert.True(StatusOf(reading) == "PermissionReported", reading.Body);
        AssertFacts(reading, source: true, target: true, binding: true, observation: true);
        await AssertScopeAsync(reading, bound, binding: true);
        await AssertObservationAsync(reading, bound, started, "MAINTAIN", "lineage");
        AssertNoDerivedWords(reading);
        AssertCall(bound, 1);
        Assert.Equal(before, await DatabaseFingerprintAsync());
        Assert.Empty(bound.IdentityObserver.Calls);
        Assert.Equal((1, 1), await BindingCountsAsync(bound.Ids.RunId));
    }

    [Fact]
    public async Task A_delivery_with_a_recorded_target_and_no_binding_is_binding_not_recorded_with_the_scope_and_nothing_is_invoked_or_written()
    {
        var bound = await BoundAsync(recordBinding: false);
        bound.Permission.Answer = () => PermissionProbe();
        var before = await DatabaseFingerprintAsync();

        var reading = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal(PermissionResponseKeys, KeysOf(reading.Root));
        Assert.Equal("BindingNotRecorded", StatusOf(reading));
        AssertFacts(reading, source: true, target: true, binding: false, observation: false);
        await AssertScopeAsync(reading, bound, binding: false);
        Assert.Empty(bound.Permission.Calls);
        Assert.Empty(bound.IdentityObserver.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task An_unusable_cli_snapshot_is_cli_unavailable_with_the_scope_and_the_binding_and_nothing_is_invoked()
    {
        var bound = await BoundAsync();
        bound.Permission.Answer = () => PermissionProbe();
        await using (var db = OpenDb())
        {
            db.HostCapabilitySnapshots.RemoveRange(db.HostCapabilitySnapshots);
            await db.SaveChangesAsync();
        }

        var none = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);
        Assert.Equal("CliUnavailable", StatusOf(none));
        AssertFacts(none, source: true, target: true, binding: true, observation: false);
        await AssertScopeAsync(none, bound, binding: true);

        await SeedSnapshotAsync(CapabilityLaunchKind.NodeScript, @"C:\safe\gh.js");
        Assert.Equal("CliUnavailable", StatusOf(await ObservePermissionAsync(bound.Host, bound.Ids.RunId)));

        await SeedSnapshotAsync(failedLast: true);
        Assert.Equal("CliUnavailable", StatusOf(await ObservePermissionAsync(bound.Host, bound.Ids.RunId)));

        // Evidence older than its due time plus the two-minute grace is stale, however successful it was.
        await SeedSnapshotAsync(evidenceAge: TimeSpan.FromHours(1));
        Assert.Equal("CliUnavailable", StatusOf(await ObservePermissionAsync(bound.Host, bound.Ids.RunId)));

        Assert.Empty(bound.Permission.Calls);
        Assert.Empty(bound.IdentityObserver.Calls);
    }

    [Fact]
    public async Task A_stored_snapshot_outside_what_domain_construction_allows_is_cli_unavailable_without_invocation_or_write()
    {
        var bound = await BoundAsync();
        bound.Permission.Answer = () => PermissionProbe();
        (string Column, string Value)[] cases =
        [
            ("ResolvedExecutablePath", "'gh.exe'"),
            ("ResolvedExecutablePath", "'.\\gh.exe'"),
            ("ObservedVersion", "NULL"),
            ("ObservedVersion", "''"),
            ("ObservedVersion", "' '"),
        ];

        foreach (var (column, value) in cases)
        {
            // Direct stored edits are not constrained by Domain construction, so the handler itself must refuse them.
            await CorruptAsync(
                ["host_capability_snapshots"],
                [$"UPDATE host_capability_snapshots SET \"{column}\" = {value} WHERE \"Capability\" = 'GitHubCli'"],
                async () =>
                {
                    var before = await DatabaseFingerprintAsync();

                    var reading = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);

                    Assert.True(reading.Status == HttpStatusCode.OK, $"{column} {value}");
                    Assert.True(StatusOf(reading) == "CliUnavailable", $"{column} {value}: {reading.Body}");
                    Assert.True(reading.Root.GetProperty("observation").ValueKind == JsonValueKind.Null, $"{column} {value}");
                    Assert.True(bound.Permission.Calls.Count == 0, $"{column} {value}");
                    Assert.True(before == await DatabaseFingerprintAsync(), $"{column} {value}");
                });
        }
    }

    [Fact]
    public async Task Each_of_the_six_literals_is_reported_exactly_with_the_observation_and_no_write_and_no_derived_word()
    {
        var bound = await BoundAsync();
        var before = await DatabaseFingerprintAsync();
        var expectedCalls = 0;

        foreach (var literal in Literals)
        {
            bound.Permission.Answer = () => Matching(literal);
            var started = DateTimeOffset.UtcNow;

            var reading = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);

            Assert.Equal(HttpStatusCode.OK, reading.Status);
            Assert.Equal(PermissionResponseKeys, KeysOf(reading.Root));
            Assert.True(StatusOf(reading) == "PermissionReported", $"{literal}: {reading.Body}");
            AssertFacts(reading, source: true, target: true, binding: true, observation: true, literal);
            await AssertScopeAsync(reading, bound, binding: true, literal);
            await AssertObservationAsync(reading, bound, started, literal, literal);
            AssertNoDerivedWords(reading);
            AssertCall(bound, ++expectedCalls);
            Assert.Equal(before, await DatabaseFingerprintAsync());
        }

        Assert.Empty(bound.IdentityObserver.Calls);
        Assert.Equal((1, 1), await BindingCountsAsync(bound.Ids.RunId));
    }

    [Fact]
    public async Task An_explicit_null_permission_is_permission_not_returned_with_the_observation_a_null_role_and_no_write()
    {
        var bound = await BoundAsync();
        bound.Permission.Answer = () => Matching(null);
        var before = await DatabaseFingerprintAsync();
        var started = DateTimeOffset.UtcNow;

        var reading = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal("PermissionNotReturned", StatusOf(reading));
        AssertFacts(reading, source: true, target: true, binding: true, observation: true);
        await AssertScopeAsync(reading, bound, binding: true);
        await AssertObservationAsync(reading, bound, started, null, "null permission");
        AssertNoDerivedWords(reading);
        AssertCall(bound, 1);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_whole_valid_answer_with_another_opaque_id_is_identity_changed_with_the_binding_and_no_observation_and_never_echoes_the_id()
    {
        var bound = await BoundAsync();
        var before = await DatabaseFingerprintAsync();
        const string Different = "R_kgDODifferent9";
        var answers = new Func<Task<GitHubRepositoryPermissionProbe?>>[]
        {
            () => PermissionProbe(nodeId: Different),
            () => PermissionProbe(nodeId: Different, permission: "ADMIN"),
            () => PermissionProbe(nodeId: Different, permission: "TRIAGE_PLUS"),
            () => PermissionProbe(nodeId: NodeId.ToLowerInvariant(), permission: "WRITE"),
            () => PermissionProbe(nodeId: NodeId + "0", permission: "READ"),
        };

        foreach (var answer in answers)
        {
            bound.Permission.Answer = answer;

            var reading = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);

            Assert.True(StatusOf(reading) == "IdentityChanged", reading.Body);
            AssertFacts(reading, source: true, target: true, binding: true, observation: false);
            await AssertScopeAsync(reading, bound, binding: true);
            Assert.DoesNotContain(Different, reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("ADMIN", reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("TRIAGE_PLUS", reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(NodeId.ToLowerInvariant(), reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(NodeId + "0", reading.Body, StringComparison.Ordinal);
        }

        Assert.Equal(answers.Length, bound.Permission.Calls.Count);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task An_unusable_foreign_or_inconsistent_answer_is_unknown_with_the_scope_and_binding_and_no_observation()
    {
        var bound = await BoundAsync();
        const string Different = "R_kgDODifferent9";
        Func<Task<GitHubRepositoryPermissionProbe?>>[] answers =
        [
            () => Task.FromResult<GitHubRepositoryPermissionProbe?>(null),
            () => PermissionProbe(owner: "someone-else", permission: "ADMIN"),
            () => PermissionProbe(repository: "renamed.repo", permission: "ADMIN"),
            () => PermissionProbe(repository: "hello.world.git", permission: "ADMIN"),
            () => PermissionProbe(nodeId: string.Empty, permission: "ADMIN"),
            () => PermissionProbe(nodeId: "has space", permission: "ADMIN"),
            () => PermissionProbe(nodeId: new string('a', 257), permission: "ADMIN"),
            () => PermissionProbe(permission: "admin"),
            () => PermissionProbe(permission: "OWNER"),
            () => PermissionProbe(permission: " WRITE"),
            () => PermissionProbe(permission: string.Empty),
            () => PermissionProbe(permission: "TRIAGE PLUS"),
            () => PermissionProbe(nodeId: Different, permission: "OWNER"),
            () => PermissionProbe(nodeId: Different, owner: "someone-else", permission: "ADMIN"),
            () => PermissionProbe(nodeId: "has space", permission: "ADMIN"),
        ];

        for (var index = 0; index < answers.Length; index++)
        {
            bound.Permission.Answer = answers[index];

            var reading = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);

            Assert.True(reading.Status == HttpStatusCode.OK, $"case {index}");
            Assert.True(StatusOf(reading) == "Unknown", $"case {index}: {reading.Body}");
            AssertFacts(reading, source: true, target: true, binding: true, observation: false, $"case {index}");
            Assert.DoesNotContain("OWNER", reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(Different, reading.Body, StringComparison.Ordinal);
        }

        Assert.Equal(answers.Length, bound.Permission.Calls.Count);
    }

    [Fact]
    public async Task A_clock_reading_that_precedes_the_binding_is_unknown_without_an_observation_and_an_equal_reading_is_admitted()
    {
        var bound = await BoundAsync();
        var recordedAt = await BindingRecordedAtAsync(bound.Ids.RunId);
        var before = await DatabaseFingerprintAsync();

        bound.Permission.Answer = () =>
        {
            bound.Clock.Fix(recordedAt.AddTicks(-1));
            return Matching("ADMIN");
        };
        var earlier = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);
        bound.Clock.Release();

        Assert.Equal(HttpStatusCode.OK, earlier.Status);
        Assert.True(StatusOf(earlier) == "Unknown", earlier.Body);
        AssertFacts(earlier, source: true, target: true, binding: true, observation: false);
        Assert.DoesNotContain("ADMIN", earlier.Body, StringComparison.Ordinal);
        Assert.Single(bound.Permission.Calls);

        bound.Permission.Answer = () =>
        {
            bound.Clock.Fix(recordedAt);
            return Matching("ADMIN");
        };
        var equal = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);
        bound.Clock.Release();

        Assert.True(StatusOf(equal) == "PermissionReported", equal.Body);
        Assert.Equal(recordedAt, equal.Root.GetProperty("observation").GetProperty("observedAtUtc").GetDateTimeOffset());
        Assert.Equal(2, bound.Permission.Calls.Count);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task Each_request_observes_once_more_and_never_reuses_an_earlier_answer_or_a_sibling_observation()
    {
        var bound = await BoundAsync();
        bound.Permission.Answer = () => Matching("ADMIN");

        var first = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);
        bound.Permission.Answer = () => Matching("READ");
        var second = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);
        bound.Permission.Answer = () => Task.FromResult<GitHubRepositoryPermissionProbe?>(null);
        var third = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal("ADMIN", first.Root.GetProperty("observation").GetProperty("viewerPermission").GetString());
        Assert.Equal("READ", second.Root.GetProperty("observation").GetProperty("viewerPermission").GetString());
        Assert.Equal("Unknown", StatusOf(third));
        Assert.Equal(3, bound.Permission.Calls.Count);
        Assert.Empty(bound.IdentityObserver.Calls);
    }

    [Fact]
    public async Task The_observation_time_is_stamped_after_the_external_work_and_the_callers_cancellation_reaches_the_observer()
    {
        var bound = await BoundAsync();
        bound.Permission.Answer = async () =>
        {
            await Task.Delay(1_200);
            return new GitHubRepositoryPermissionProbe(NodeId, DefaultOwner, DefaultRepository, "READ");
        };

        var reading = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal("PermissionReported", StatusOf(reading));
        var observedAt = reading.Root.GetProperty("observation").GetProperty("observedAtUtc").GetDateTimeOffset();
        Assert.True(observedAt >= bound.Permission.LastStartedAt!.Value.AddMilliseconds(1_100), $"{observedAt:o} vs {bound.Permission.LastStartedAt:o}");
        Assert.True(Assert.Single(bound.Permission.Tokens).CanBeCanceled);
    }

    [Fact]
    public async Task An_unexpected_observer_fault_is_not_converted_into_a_status_and_writes_nothing()
    {
        var bound = await BoundAsync();
        bound.Permission.Answer = () => throw new InvalidOperationException("boom");
        var before = await DatabaseFingerprintAsync();

        var reading = await ObservePermissionAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.InternalServerError, reading.Status);
        Assert.Equal(before, await DatabaseFingerprintAsync());
        Assert.Single(bound.Permission.Calls);

        // The singleton observer is not poisoned: the next request observes normally.
        bound.Permission.Answer = () => Matching("WRITE");
        Assert.Equal("PermissionReported", StatusOf(await ObservePermissionAsync(bound.Host, bound.Ids.RunId)));
    }
}
