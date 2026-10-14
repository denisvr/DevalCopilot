using System.Net;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0044 through the production host: a real delivered run, its recorded ADR-0040 target and ADR-0043 binding, a scripted
/// provider-neutral check observer, and the real MVC authentication, mediator, readers and file-backed SQLite. Every read is proved to
/// write nothing, to reuse nothing from an earlier answer or a sibling observation, and to invoke the observer exactly when (and only
/// with what) the recorded scope, the binding and the current CLI snapshot admit.
/// </summary>
public sealed class GitHubCiObservationEndpointTests : GitHubCiObservationTestBase
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
        Assert.Equal(expectedCalls, bound.Ci.Calls.Count);
        Assert.Equal((GhPath, DefaultOwner, DefaultRepository, Scene.BranchName), bound.Ci.Calls[^1]);
    }

    private async Task AssertObservationAsync(
        ReceiptReading reading, Bound bound, DateTimeOffset startedAt, string? branch, string? commit, string? rollup, string context)
    {
        var observation = reading.Root.GetProperty("observation");
        Assert.Equal(ObservationKeys, KeysOf(observation));
        Assert.Equal(NodeId, observation.GetProperty("repositoryNodeId").GetString());
        Assert.Equal("OCTO-CAT", observation.GetProperty("owner").GetString());
        Assert.Equal("Hello.World", observation.GetProperty("repository").GetString());
        var observedAt = observation.GetProperty("observedAtUtc").GetDateTimeOffset();
        Assert.InRange(observedAt, startedAt.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.True(observedAt >= await BindingRecordedAtAsync(bound.Ids.RunId), context);
        var remote = observation.GetProperty("ref");
        if (branch is null)
        {
            Assert.True(remote.ValueKind == JsonValueKind.Null, context);
        }
        else
        {
            Assert.Equal(["branchName", "commitSha"], KeysOf(remote));
            Assert.Equal(branch, remote.GetProperty("branchName").GetString());
            Assert.Equal(commit, remote.GetProperty("commitSha").GetString());
        }

        var state = observation.GetProperty("rollupState");
        if (rollup is null)
        {
            Assert.True(state.ValueKind == JsonValueKind.Null, $"{context}: {reading.Body}");
        }
        else
        {
            Assert.Equal(JsonValueKind.String, state.ValueKind);
            Assert.Equal(rollup, state.GetString());
        }
    }

    private static void AssertNoTranslatedWords(ReceiptReading reading)
    {
        foreach (var word in new[] { "Passed", "Approved", "Ready", "Green", "passed", "approved", "green" })
        {
            Assert.DoesNotContain(word, reading.Body, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(GhPath, reading.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_delivery_with_a_recorded_target_and_no_binding_is_binding_not_recorded_with_the_scope_and_nothing_is_invoked_or_written()
    {
        var bound = await BoundAsync(recordBinding: false);
        bound.Ci.Answer = () => CiProbe();
        var before = await DatabaseFingerprintAsync();

        var reading = await ObserveCiAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal(CiResponseKeys, KeysOf(reading.Root));
        Assert.Equal("BindingNotRecorded", StatusOf(reading));
        AssertFacts(reading, source: true, target: true, binding: false, observation: false);
        await AssertScopeAsync(reading, bound, binding: false);
        Assert.Empty(bound.Ci.Calls);
        Assert.Empty(bound.IdentityObserver.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task An_unusable_cli_snapshot_is_cli_unavailable_with_the_scope_and_the_binding_and_nothing_is_invoked()
    {
        var bound = await BoundAsync();
        bound.Ci.Answer = () => CiProbe();
        await using (var db = OpenDb())
        {
            db.HostCapabilitySnapshots.RemoveRange(db.HostCapabilitySnapshots);
            await db.SaveChangesAsync();
        }

        var none = await ObserveCiAsync(bound.Host, bound.Ids.RunId);
        Assert.Equal("CliUnavailable", StatusOf(none));
        AssertFacts(none, source: true, target: true, binding: true, observation: false);
        await AssertScopeAsync(none, bound, binding: true);

        await SeedSnapshotAsync(CapabilityLaunchKind.NodeScript, @"C:\safe\gh.js");
        Assert.Equal("CliUnavailable", StatusOf(await ObserveCiAsync(bound.Host, bound.Ids.RunId)));

        await SeedSnapshotAsync(failedLast: true);
        Assert.Equal("CliUnavailable", StatusOf(await ObserveCiAsync(bound.Host, bound.Ids.RunId)));

        // Evidence older than its due time plus the two-minute grace is stale, however successful it was.
        await SeedSnapshotAsync(evidenceAge: TimeSpan.FromHours(1));
        Assert.Equal("CliUnavailable", StatusOf(await ObserveCiAsync(bound.Host, bound.Ids.RunId)));

        Assert.Empty(bound.Ci.Calls);
        Assert.Empty(bound.IdentityObserver.Calls);
    }

    [Fact]
    public async Task A_stored_snapshot_outside_what_domain_construction_allows_is_cli_unavailable_without_invocation_or_write()
    {
        var bound = await BoundAsync();
        bound.Ci.Answer = () => CiProbe();
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

                    var reading = await ObserveCiAsync(bound.Host, bound.Ids.RunId);

                    Assert.True(reading.Status == HttpStatusCode.OK, $"{column} {value}");
                    Assert.True(StatusOf(reading) == "CliUnavailable", $"{column} {value}: {reading.Body}");
                    Assert.True(reading.Root.GetProperty("observation").ValueKind == JsonValueKind.Null, $"{column} {value}");
                    Assert.True(bound.Ci.Calls.Count == 0, $"{column} {value}");
                    Assert.True(before == await DatabaseFingerprintAsync(), $"{column} {value}");
                });
        }
    }

    [Fact]
    public async Task A_matching_commit_reports_each_of_the_five_literal_states_with_the_exact_ref_and_no_write_and_no_translated_word()
    {
        var bound = await BoundAsync();
        var branch = Scene.BranchName;
        var before = await DatabaseFingerprintAsync();
        var expectedCalls = 0;

        foreach (var state in new[] { "ERROR", "EXPECTED", "FAILURE", "PENDING", "SUCCESS" })
        {
            bound.Ci.Answer = () => CiProbe("OCTO-CAT", "Hello.World", reference: new GitHubCiRef(branch, bound.Sha), rollup: state);
            var started = DateTimeOffset.UtcNow;

            var reading = await ObserveCiAsync(bound.Host, bound.Ids.RunId);

            Assert.Equal(HttpStatusCode.OK, reading.Status);
            Assert.Equal(CiResponseKeys, KeysOf(reading.Root));
            Assert.True(StatusOf(reading) == "StatusReported", $"{state}: {reading.Body}");
            AssertFacts(reading, source: true, target: true, binding: true, observation: true, state);
            await AssertScopeAsync(reading, bound, binding: true, state);
            await AssertObservationAsync(reading, bound, started, branch, bound.Sha, state, state);
            AssertNoTranslatedWords(reading);
            AssertCall(bound, ++expectedCalls);
            Assert.Equal(before, await DatabaseFingerprintAsync());
        }

        Assert.Empty(bound.IdentityObserver.Calls);
        Assert.Equal((1, 1), await BindingCountsAsync(bound.Ids.RunId));
    }

    [Fact]
    public async Task A_matching_commit_with_no_rollup_is_no_status_reported_with_the_exact_ref_and_a_null_state()
    {
        var bound = await BoundAsync();
        var branch = Scene.BranchName;
        bound.Ci.Answer = () => CiProbe("OCTO-CAT", "Hello.World", reference: new GitHubCiRef(branch, bound.Sha), rollup: null);
        var before = await DatabaseFingerprintAsync();
        var started = DateTimeOffset.UtcNow;

        var reading = await ObserveCiAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal("NoStatusReported", StatusOf(reading));
        await AssertScopeAsync(reading, bound, binding: true);
        await AssertObservationAsync(reading, bound, started, branch, bound.Sha, null, "no rollup");
        AssertNoTranslatedWords(reading);
        AssertCall(bound, 1);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_different_commit_is_commit_different_with_the_exact_ref_and_never_exposes_a_state_even_when_github_supplied_one()
    {
        var bound = await BoundAsync();
        var branch = Scene.BranchName;
        var other = new string('e', 40);
        var before = await DatabaseFingerprintAsync();
        var expectedCalls = 0;

        foreach (var supplied in new[] { "SUCCESS", "FAILURE", "ERROR", "EXPECTED", "PENDING", null })
        {
            bound.Ci.Answer = () => CiProbe("OCTO-CAT", "Hello.World", reference: new GitHubCiRef(branch, other), rollup: supplied);
            var started = DateTimeOffset.UtcNow;

            var reading = await ObserveCiAsync(bound.Host, bound.Ids.RunId);

            Assert.True(StatusOf(reading) == "CommitDifferent", $"{supplied}: {reading.Body}");
            await AssertScopeAsync(reading, bound, binding: true, supplied ?? "none");
            await AssertObservationAsync(reading, bound, started, branch, other, null, supplied ?? "none");
            if (supplied is not null)
            {
                Assert.DoesNotContain(supplied, reading.Body, StringComparison.Ordinal);
            }

            AssertNoTranslatedWords(reading);
            AssertCall(bound, ++expectedCalls);
            Assert.Equal(before, await DatabaseFingerprintAsync());
        }
    }

    [Fact]
    public async Task A_valid_null_ref_is_ref_not_returned_with_a_null_ref_and_a_null_state_and_no_write()
    {
        var bound = await BoundAsync();
        bound.Ci.Answer = () => CiProbe("OCTO-CAT", "Hello.World");
        var before = await DatabaseFingerprintAsync();
        var started = DateTimeOffset.UtcNow;

        var reading = await ObserveCiAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal("RefNotReturned", StatusOf(reading));
        await AssertScopeAsync(reading, bound, binding: true);
        await AssertObservationAsync(reading, bound, started, null, null, null, "ref not returned");
        AssertCall(bound, 1);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_whole_valid_answer_with_another_opaque_id_is_identity_changed_with_the_binding_and_no_observation_and_never_echoes_the_id()
    {
        var bound = await BoundAsync();
        var branch = Scene.BranchName;
        var before = await DatabaseFingerprintAsync();
        const string Different = "R_kgDODifferent9";
        var answers = new Func<Task<GitHubCiProbe?>>[]
        {
            () => CiProbe(nodeId: Different),
            () => CiProbe(nodeId: Different, reference: new GitHubCiRef(branch, bound.Sha), rollup: "SUCCESS"),
            () => CiProbe(nodeId: Different, reference: new GitHubCiRef(branch, new string('e', 40)), rollup: null),
            () => CiProbe(nodeId: NodeId.ToLowerInvariant()),
            () => CiProbe(nodeId: NodeId + "0"),
        };

        foreach (var answer in answers)
        {
            bound.Ci.Answer = answer;

            var reading = await ObserveCiAsync(bound.Host, bound.Ids.RunId);

            Assert.True(StatusOf(reading) == "IdentityChanged", reading.Body);
            AssertFacts(reading, source: true, target: true, binding: true, observation: false);
            await AssertScopeAsync(reading, bound, binding: true);
            Assert.DoesNotContain(Different, reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("SUCCESS", reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(NodeId.ToLowerInvariant(), reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(NodeId + "0", reading.Body, StringComparison.Ordinal);
        }

        Assert.Equal(answers.Length, bound.Ci.Calls.Count);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task An_unusable_foreign_fallback_or_inconsistent_answer_is_unknown_with_the_scope_and_binding_and_no_observation()
    {
        var bound = await BoundAsync();
        var branch = Scene.BranchName;
        var sha = bound.Sha;
        const string Different = "R_kgDODifferent9";
        Func<Task<GitHubCiProbe?>>[] answers =
        [
            () => Task.FromResult<GitHubCiProbe?>(null),
            () => CiProbe(owner: "someone-else"),
            () => CiProbe(repository: "renamed.repo"),
            () => CiProbe(repository: "hello.world.git"),
            () => CiProbe(nodeId: string.Empty),
            () => CiProbe(nodeId: "has space"),
            () => CiProbe(nodeId: new string('a', 257)),
            () => CiProbe(reference: new GitHubCiRef("main", sha)),
            () => CiProbe(reference: new GitHubCiRef(branch + "0", sha)),
            () => CiProbe(reference: new GitHubCiRef(branch.ToUpperInvariant(), sha)),
            () => CiProbe(reference: new GitHubCiRef("refs/heads/" + branch, sha)),
            () => CiProbe(reference: new GitHubCiRef(branch, sha.ToUpperInvariant())),
            () => CiProbe(reference: new GitHubCiRef(branch, sha[..39])),
            () => CiProbe(reference: new GitHubCiRef(branch, new string('g', 40))),
            () => CiProbe(reference: new GitHubCiRef(branch, string.Empty)),
            () => CiProbe(reference: new GitHubCiRef(branch, sha), rollup: "success"),
            () => CiProbe(reference: new GitHubCiRef(branch, sha), rollup: "PASSED"),
            () => CiProbe(reference: new GitHubCiRef(branch, sha), rollup: " SUCCESS"),
            () => CiProbe(reference: new GitHubCiRef(branch, sha), rollup: string.Empty),
            () => CiProbe(reference: new GitHubCiRef(branch, new string('e', 40)), rollup: "PASSED"),
            () => CiProbe(reference: null, rollup: "SUCCESS"),
            () => CiProbe(nodeId: Different, reference: new GitHubCiRef("main", sha)),
            () => CiProbe(nodeId: Different, reference: new GitHubCiRef(branch, sha), rollup: "GREEN"),
            () => CiProbe(nodeId: "has space", reference: new GitHubCiRef(branch, sha)),
        ];

        for (var index = 0; index < answers.Length; index++)
        {
            bound.Ci.Answer = answers[index];

            var reading = await ObserveCiAsync(bound.Host, bound.Ids.RunId);

            Assert.True(reading.Status == HttpStatusCode.OK, $"case {index}");
            Assert.True(StatusOf(reading) == "Unknown", $"case {index}: {reading.Body}");
            AssertFacts(reading, source: true, target: true, binding: true, observation: false, $"case {index}");
            Assert.DoesNotContain("PASSED", reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(Different, reading.Body, StringComparison.Ordinal);
        }

        Assert.Equal(answers.Length, bound.Ci.Calls.Count);
    }

    [Fact]
    public async Task A_clock_reading_that_precedes_the_binding_is_unknown_without_an_observation_and_an_equal_reading_is_admitted()
    {
        var bound = await BoundAsync();
        var branch = Scene.BranchName;
        var recordedAt = await BindingRecordedAtAsync(bound.Ids.RunId);
        var before = await DatabaseFingerprintAsync();

        bound.Ci.Answer = () =>
        {
            bound.Clock.Fix(recordedAt.AddTicks(-1));
            return CiProbe("OCTO-CAT", "Hello.World", reference: new GitHubCiRef(branch, bound.Sha), rollup: "SUCCESS");
        };
        var earlier = await ObserveCiAsync(bound.Host, bound.Ids.RunId);
        bound.Clock.Release();

        Assert.Equal(HttpStatusCode.OK, earlier.Status);
        Assert.True(StatusOf(earlier) == "Unknown", earlier.Body);
        AssertFacts(earlier, source: true, target: true, binding: true, observation: false);
        Assert.DoesNotContain("SUCCESS", earlier.Body, StringComparison.Ordinal);
        Assert.Single(bound.Ci.Calls);

        bound.Ci.Answer = () =>
        {
            bound.Clock.Fix(recordedAt);
            return CiProbe("OCTO-CAT", "Hello.World", reference: new GitHubCiRef(branch, bound.Sha), rollup: "SUCCESS");
        };
        var equal = await ObserveCiAsync(bound.Host, bound.Ids.RunId);
        bound.Clock.Release();

        Assert.True(StatusOf(equal) == "StatusReported", equal.Body);
        Assert.Equal(recordedAt, equal.Root.GetProperty("observation").GetProperty("observedAtUtc").GetDateTimeOffset());
        Assert.Equal(2, bound.Ci.Calls.Count);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task Each_request_observes_once_more_and_never_reuses_an_earlier_answer_or_a_sibling_observation()
    {
        var bound = await BoundAsync();
        var branch = Scene.BranchName;
        bound.Ci.Answer = () => CiProbe("OCTO-CAT", "Hello.World", reference: new GitHubCiRef(branch, bound.Sha), rollup: "SUCCESS");

        var first = await ObserveCiAsync(bound.Host, bound.Ids.RunId);
        bound.Ci.Answer = () => CiProbe("OCTO-CAT", "Hello.World", reference: new GitHubCiRef(branch, bound.Sha), rollup: "FAILURE");
        var second = await ObserveCiAsync(bound.Host, bound.Ids.RunId);
        bound.Ci.Answer = () => Task.FromResult<GitHubCiProbe?>(null);
        var third = await ObserveCiAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal("SUCCESS", first.Root.GetProperty("observation").GetProperty("rollupState").GetString());
        Assert.Equal("FAILURE", second.Root.GetProperty("observation").GetProperty("rollupState").GetString());
        Assert.Equal("Unknown", StatusOf(third));
        Assert.Equal(3, bound.Ci.Calls.Count);
        Assert.Empty(bound.IdentityObserver.Calls);
    }

    [Fact]
    public async Task The_observation_time_is_stamped_after_the_external_work_and_the_callers_cancellation_reaches_the_observer()
    {
        var bound = await BoundAsync();
        bound.Ci.Answer = async () =>
        {
            await Task.Delay(1_200);
            return new GitHubCiProbe(NodeId, DefaultOwner, DefaultRepository, null, null);
        };

        var reading = await ObserveCiAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal("RefNotReturned", StatusOf(reading));
        var observedAt = reading.Root.GetProperty("observation").GetProperty("observedAtUtc").GetDateTimeOffset();
        Assert.True(observedAt >= bound.Ci.LastStartedAt!.Value.AddMilliseconds(1_100), $"{observedAt:o} vs {bound.Ci.LastStartedAt:o}");
        Assert.True(Assert.Single(bound.Ci.Tokens).CanBeCanceled);
    }

    [Fact]
    public async Task An_unexpected_observer_fault_is_not_converted_into_a_status_and_writes_nothing()
    {
        var bound = await BoundAsync();
        bound.Ci.Answer = () => throw new InvalidOperationException("boom");
        var before = await DatabaseFingerprintAsync();

        var reading = await ObserveCiAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.InternalServerError, reading.Status);
        Assert.Equal(before, await DatabaseFingerprintAsync());
        Assert.Single(bound.Ci.Calls);
    }

    [Fact]
    public async Task A_query_string_is_ignored_and_the_observer_receives_only_the_recorded_scope()
    {
        var bound = await BoundAsync();
        var branch = Scene.BranchName;
        bound.Ci.Answer = () => CiProbe("OCTO-CAT", "Hello.World", reference: new GitHubCiRef(branch, bound.Sha), rollup: "PENDING");

        var reading = await ObserveCiAsync(bound.Host, bound.Ids.RunId, query: "?owner=sentinel-owner&ref=main&sha=" + new string('f', 40));

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal("StatusReported", StatusOf(reading));
        AssertCall(bound, 1);
        Assert.DoesNotContain("sentinel-owner", reading.Body, StringComparison.Ordinal);
    }
}
