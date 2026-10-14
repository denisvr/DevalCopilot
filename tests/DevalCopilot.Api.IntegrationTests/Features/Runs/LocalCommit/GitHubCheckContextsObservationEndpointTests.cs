using System.Net;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0045 through the production host: a real delivered run, its recorded ADR-0040 target and ADR-0043 binding, a scripted
/// provider-neutral contexts observer, and the real MVC authentication, mediator, readers and file-backed SQLite. Every read is proved to
/// write nothing, to reuse nothing from an earlier answer or a sibling observation, to invoke the observer exactly when (and only with
/// what) the recorded scope, the binding and the current CLI snapshot admit, to validate the whole probe before classifying it and to
/// withhold every count and entry unless the bound identity, the exact branch and the recorded commit all agree and the bound allows it.
/// </summary>
public sealed class GitHubCheckContextsObservationEndpointTests
    : GitHubCheckContextsObservationTestBase, IClassFixture<GitHubCheckContextsSealedEvidence>
{
    private const string Sentinel = "SENTINEL-LABEL";

    private readonly GitHubCheckContextsSealedClone _clone;

    public GitHubCheckContextsObservationEndpointTests(GitHubCheckContextsSealedEvidence evidence)
        : this(evidence.ReserveClone(), evidence)
    {
    }

    private GitHubCheckContextsObservationEndpointTests(GitHubCheckContextsSealedClone clone, GitHubCheckContextsSealedEvidence evidence)
        : base(new LocalCommitBorrowedResources(evidence.Scene, clone.DatabasePath, clone.WritableRoot, clone))
    {
        _clone = clone;
    }

    /// <summary>This fact's own copy of the class-owned sealed lineage (ADR-0046): the delivery, target and, unless excluded, binding the
    /// production commands recorded once for the class, with a host, observers, counters and clock that no other fact shares.</summary>
    private Task<Bound> SnapshotBoundAsync(bool recordBinding = true, IGitHubCheckContextsObserver? observer = null) =>
        _clone.ProvisionAsync(
            recordBinding ? GitHubCheckContextsSealedStage.BindingRecorded : GitHubCheckContextsSealedStage.TargetRecorded, observer);

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
        Assert.Equal(expectedCalls, bound.Contexts.Calls.Count);
        Assert.Equal((GhPath, DefaultOwner, DefaultRepository, Scene.BranchName), bound.Contexts.Calls[^1]);
    }

    /// <summary>The observation's fixed envelope: observed names (never the recorded ones), a host time after the binding, and the
    /// requested ref and collection nullability. The contexts themselves are checked by <see cref="AssertContexts"/>.</summary>
    private async Task<JsonElement> AssertObservationAsync(
        ReceiptReading reading, Bound bound, DateTimeOffset startedAt, string? branch, string? commit, int? totalCount, bool contexts,
        string context)
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

        var count = observation.GetProperty("totalCount");
        if (totalCount is null)
        {
            Assert.True(count.ValueKind == JsonValueKind.Null, $"{context}: {reading.Body}");
        }
        else
        {
            Assert.Equal(JsonValueKind.Number, count.ValueKind);
            Assert.Equal(totalCount, count.GetInt32());
        }

        Assert.Equal(contexts ? JsonValueKind.Array : JsonValueKind.Null, observation.GetProperty("contexts").ValueKind);
        return observation;
    }

    private static void AssertContexts(JsonElement observation, params GitHubCheckContext[] expected)
    {
        var array = observation.GetProperty("contexts").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, array.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(ContextKeys, KeysOf(array[index]));
            Assert.Equal(expected[index].Kind, array[index].GetProperty("kind").GetString());
            Assert.Equal(expected[index].Id, array[index].GetProperty("id").GetString());
            Assert.Equal(expected[index].Name, array[index].GetProperty("name").GetString());
            Assert.Equal(expected[index].Status, array[index].GetProperty("status").GetString());
            AssertNullable(array[index].GetProperty("conclusion"), expected[index].Conclusion);
            AssertNullable(array[index].GetProperty("checkSuiteId"), expected[index].CheckSuiteId);
        }
    }

    private static void AssertNullable(JsonElement element, string? expected)
    {
        if (expected is null)
        {
            Assert.Equal(JsonValueKind.Null, element.ValueKind);
        }
        else
        {
            Assert.Equal(expected, element.GetString());
        }
    }

    private static void AssertNoTranslatedWords(ReceiptReading reading)
    {
        foreach (var word in new[] { "Passed", "Approved", "Ready", "Green", "passed", "approved", "green", "NoChecks", "Complete" })
        {
            Assert.DoesNotContain(word, reading.Body, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(GhPath, reading.Body, StringComparison.OrdinalIgnoreCase);
    }

    private GitHubCheckContextsRef Remote(Bound bound, string? sha = null) => new(Scene.BranchName, sha ?? bound.Sha);

    private static GitHubCheckContext[] SentinelEntries(int count) =>
        Enumerable.Range(0, count).Select(index => RunEntry(id: $"CR_{index}", name: $"{Sentinel}-{index}")).ToArray();

    [Fact]
    public async Task A_delivery_with_a_recorded_target_and_no_binding_is_binding_not_recorded_with_the_scope_and_nothing_is_invoked_or_written()
    {
        var bound = await SnapshotBoundAsync(recordBinding: false);
        bound.Contexts.Answer = () => ContextsProbe();
        var before = await DatabaseFingerprintAsync();

        var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal(ContextsResponseKeys, KeysOf(reading.Root));
        Assert.Equal("BindingNotRecorded", StatusOf(reading));
        AssertFacts(reading, source: true, target: true, binding: false, observation: false);
        await AssertScopeAsync(reading, bound, binding: false);
        Assert.Empty(bound.Contexts.Calls);
        Assert.Empty(bound.IdentityObserver.Calls);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task An_unusable_cli_snapshot_is_cli_unavailable_with_the_scope_and_the_binding_and_nothing_is_invoked()
    {
        var bound = await SnapshotBoundAsync();
        bound.Contexts.Answer = () => ContextsProbe();
        await using (var db = OpenDb())
        {
            db.HostCapabilitySnapshots.RemoveRange(db.HostCapabilitySnapshots);
            await db.SaveChangesAsync();
        }

        var none = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);
        Assert.Equal("CliUnavailable", StatusOf(none));
        AssertFacts(none, source: true, target: true, binding: true, observation: false);
        await AssertScopeAsync(none, bound, binding: true);

        await SeedSnapshotAsync(CapabilityLaunchKind.NodeScript, @"C:\safe\gh.js");
        Assert.Equal("CliUnavailable", StatusOf(await ObserveContextsAsync(bound.Host, bound.Ids.RunId)));

        await SeedSnapshotAsync(failedLast: true);
        Assert.Equal("CliUnavailable", StatusOf(await ObserveContextsAsync(bound.Host, bound.Ids.RunId)));

        // Evidence older than its due time plus the two-minute grace is stale, however successful it was.
        await SeedSnapshotAsync(evidenceAge: TimeSpan.FromHours(1));
        Assert.Equal("CliUnavailable", StatusOf(await ObserveContextsAsync(bound.Host, bound.Ids.RunId)));

        Assert.Empty(bound.Contexts.Calls);
        Assert.Empty(bound.IdentityObserver.Calls);
    }

    [Fact]
    public async Task A_stored_snapshot_outside_what_domain_construction_allows_is_cli_unavailable_without_invocation_or_write()
    {
        var bound = await SnapshotBoundAsync();
        bound.Contexts.Answer = () => ContextsProbe();
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

                    var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

                    Assert.True(reading.Status == HttpStatusCode.OK, $"{column} {value}");
                    Assert.True(StatusOf(reading) == "CliUnavailable", $"{column} {value}: {reading.Body}");
                    Assert.True(reading.Root.GetProperty("observation").ValueKind == JsonValueKind.Null, $"{column} {value}");
                    Assert.True(bound.Contexts.Calls.Count == 0, $"{column} {value}");
                    Assert.True(before == await DatabaseFingerprintAsync(), $"{column} {value}");
                });
        }
    }

    [Fact]
    public async Task A_matching_commit_with_a_mixed_collection_reports_every_entry_in_provider_order_with_the_exact_wire_shape_and_no_write()
    {
        var bound = await SnapshotBoundAsync();
        var entries = new[]
        {
            RunEntry("CR_a", "build", "COMPLETED", "SUCCESS", "CS_1"),
            RunEntry("CR_b", "build", "COMPLETED", "FAILURE", "CS_1"),
            StatusEntry("SC_a", "build", "PENDING"),
            RunEntry("CR_c", "deploy", "IN_PROGRESS", null, "CS_2"),
            RunEntry("CR_d", "lint", "COMPLETED", null, "CS_2"),
            StatusEntry("SC_b", "ci/legacy", "EXPECTED"),
            RunEntry("CR_e", "e2e", "QUEUED", "SUCCESS", "CS_3"),
        };
        bound.Contexts.Answer = () => ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound), page: PageOf(entries.Length, entries));
        var before = await DatabaseFingerprintAsync();
        var started = DateTimeOffset.UtcNow;

        var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal(ContextsResponseKeys, KeysOf(reading.Root));
        Assert.True(StatusOf(reading) == "ContextsReported", reading.Body);
        AssertFacts(reading, source: true, target: true, binding: true, observation: true);
        await AssertScopeAsync(reading, bound, binding: true);
        var observation = await AssertObservationAsync(
            reading, bound, started, Scene.BranchName, bound.Sha, entries.Length, contexts: true, "mixed");
        AssertContexts(observation, entries);
        AssertNoTranslatedWords(reading);
        AssertCall(bound, 1);
        Assert.Equal(before, await DatabaseFingerprintAsync());
        Assert.Empty(bound.IdentityObserver.Calls);
        Assert.Equal((1, 1), await BindingCountsAsync(bound.Ids.RunId));
    }

    [Fact]
    public async Task A_matching_commit_with_zero_entries_is_contexts_reported_with_a_zero_count_and_an_empty_array_never_a_null_collection()
    {
        var bound = await SnapshotBoundAsync();
        bound.Contexts.Answer = () => ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound), page: PageOf(0));
        var started = DateTimeOffset.UtcNow;

        var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

        Assert.True(StatusOf(reading) == "ContextsReported", reading.Body);
        var observation = await AssertObservationAsync(reading, bound, started, Scene.BranchName, bound.Sha, 0, contexts: true, "zero");
        AssertContexts(observation);
        Assert.Contains("\"contexts\":[]", reading.Body, StringComparison.Ordinal);
        AssertNoTranslatedWords(reading);
    }

    [Fact]
    public async Task Exactly_fifty_entries_are_reported_and_a_larger_total_is_limit_exceeded_with_every_entry_withheld()
    {
        var bound = await SnapshotBoundAsync();
        var fifty = SentinelEntries(50);
        bound.Contexts.Answer = () => ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound), page: PageOf(50, fifty));
        var started = DateTimeOffset.UtcNow;

        var exact = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

        Assert.True(StatusOf(exact) == "ContextsReported", exact.Body);
        AssertContexts(
            await AssertObservationAsync(exact, bound, started, Scene.BranchName, bound.Sha, 50, contexts: true, "fifty"), fifty);

        foreach (var total in new[] { 51, 52, 1000, int.MaxValue })
        {
            bound.Contexts.Answer = () => ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound), page: PageOf(total, fifty));
            started = DateTimeOffset.UtcNow;

            var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

            Assert.True(StatusOf(reading) == "LimitExceeded", $"{total}: {reading.Body}");
            await AssertObservationAsync(reading, bound, started, Scene.BranchName, bound.Sha, total, contexts: false, total.ToString());
            Assert.DoesNotContain(Sentinel, reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("CR_0", reading.Body, StringComparison.Ordinal);
            AssertNoTranslatedWords(reading);
        }

        Assert.Equal(5, bound.Contexts.Calls.Count);
    }

    [Fact]
    public async Task A_matching_commit_with_no_rollup_is_no_contexts_returned_with_the_exact_ref_and_a_null_count_and_collection()
    {
        var bound = await SnapshotBoundAsync();
        bound.Contexts.Answer = () => ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound));
        var before = await DatabaseFingerprintAsync();
        var started = DateTimeOffset.UtcNow;

        var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal("NoContextsReturned", StatusOf(reading));
        await AssertScopeAsync(reading, bound, binding: true);
        await AssertObservationAsync(reading, bound, started, Scene.BranchName, bound.Sha, null, contexts: false, "no rollup");
        AssertNoTranslatedWords(reading);
        AssertCall(bound, 1);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_different_commit_is_commit_different_with_the_exact_ref_and_withholds_every_count_and_entry_even_when_github_supplied_them()
    {
        var bound = await SnapshotBoundAsync();
        var other = new string('e', 40);
        var before = await DatabaseFingerprintAsync();
        var pages = new GitHubCheckContextsPage?[]
        {
            PageOf(3, SentinelEntries(3)),
            PageOf(0),
            PageOf(50, SentinelEntries(50)),
            PageOf(51, SentinelEntries(50)),
            PageOf(int.MaxValue, SentinelEntries(50)),
            null,
        };
        var expectedCalls = 0;

        foreach (var page in pages)
        {
            bound.Contexts.Answer = () => ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound, other), page: page);
            var started = DateTimeOffset.UtcNow;

            var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

            Assert.True(StatusOf(reading) == "CommitDifferent", $"{page?.TotalCount}: {reading.Body}");
            await AssertScopeAsync(reading, bound, binding: true);
            await AssertObservationAsync(reading, bound, started, Scene.BranchName, other, null, contexts: false, "different");
            Assert.DoesNotContain(Sentinel, reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("CR_0", reading.Body, StringComparison.Ordinal);
            AssertNoTranslatedWords(reading);
            AssertCall(bound, ++expectedCalls);
            Assert.Equal(before, await DatabaseFingerprintAsync());
        }
    }

    [Fact]
    public async Task A_valid_null_ref_is_ref_not_returned_with_a_null_ref_count_and_collection_and_no_write()
    {
        var bound = await SnapshotBoundAsync();
        bound.Contexts.Answer = () => ContextsProbe("OCTO-CAT", "Hello.World");
        var before = await DatabaseFingerprintAsync();
        var started = DateTimeOffset.UtcNow;

        var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal("RefNotReturned", StatusOf(reading));
        await AssertScopeAsync(reading, bound, binding: true);
        await AssertObservationAsync(reading, bound, started, null, null, null, contexts: false, "ref not returned");
        AssertCall(bound, 1);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task A_whole_valid_answer_with_another_opaque_id_is_identity_changed_with_the_binding_and_no_observation_never_echoing_the_id_or_entries()
    {
        var bound = await SnapshotBoundAsync();
        var before = await DatabaseFingerprintAsync();
        const string Different = "R_kgDODifferent9";
        var answers = new Func<Task<GitHubCheckContextsProbe?>>[]
        {
            () => ContextsProbe(nodeId: Different),
            () => ContextsProbe(nodeId: Different, reference: Remote(bound), page: PageOf(2, SentinelEntries(2))),
            () => ContextsProbe(nodeId: Different, reference: Remote(bound, new string('e', 40)), page: PageOf(1, SentinelEntries(1))),
            () => ContextsProbe(nodeId: Different, reference: Remote(bound), page: PageOf(51, SentinelEntries(50))),
            () => ContextsProbe(nodeId: NodeId.ToLowerInvariant()),
            () => ContextsProbe(nodeId: NodeId + "0", reference: Remote(bound), page: PageOf(0)),
        };

        foreach (var answer in answers)
        {
            bound.Contexts.Answer = answer;

            var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

            Assert.True(StatusOf(reading) == "IdentityChanged", reading.Body);
            AssertFacts(reading, source: true, target: true, binding: true, observation: false);
            await AssertScopeAsync(reading, bound, binding: true);
            Assert.DoesNotContain(Different, reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(Sentinel, reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(NodeId.ToLowerInvariant(), reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(NodeId + "0", reading.Body, StringComparison.Ordinal);
        }

        Assert.Equal(answers.Length, bound.Contexts.Calls.Count);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task An_unusable_foreign_fallback_inconsistent_or_malformed_probe_is_unknown_before_any_classification_whatever_the_identity_or_commit()
    {
        var bound = await SnapshotBoundAsync();
        var sha = bound.Sha;
        var branch = Scene.BranchName;
        var other = new string('e', 40);
        const string Different = "R_kgDODifferent9";
        var good = SentinelEntries(2);
        Func<Task<GitHubCheckContextsProbe?>>[] answers =
        [
            () => Task.FromResult<GitHubCheckContextsProbe?>(null),
            () => ContextsProbe(owner: "someone-else"),
            () => ContextsProbe(repository: "renamed.repo"),
            () => ContextsProbe(repository: "hello.world.git"),
            () => ContextsProbe(nodeId: string.Empty),
            () => ContextsProbe(nodeId: "has space"),
            () => ContextsProbe(nodeId: new string('a', 257)),
            () => ContextsProbe(reference: new GitHubCheckContextsRef("main", sha)),
            () => ContextsProbe(reference: new GitHubCheckContextsRef(branch + "0", sha)),
            () => ContextsProbe(reference: new GitHubCheckContextsRef(branch.ToUpperInvariant(), sha)),
            () => ContextsProbe(reference: new GitHubCheckContextsRef(branch, sha.ToUpperInvariant())),
            () => ContextsProbe(reference: new GitHubCheckContextsRef(branch, sha[..39])),
            () => ContextsProbe(reference: new GitHubCheckContextsRef(branch, string.Empty)),
            // A page without a ref is never admitted.
            () => ContextsProbe(page: PageOf(2, good)),
            () => ContextsProbe(page: PageOf(0)),
            // Counts that do not equal the lesser of fifty and the total, on the recorded commit, another commit and another identity.
            () => ContextsProbe(reference: Remote(bound), page: PageOf(3, good)),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(1, good)),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(0, good)),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(-1)),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(51, SentinelEntries(51))),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(60, SentinelEntries(49))),
            () => ContextsProbe(reference: Remote(bound, other), page: PageOf(3, good)),
            () => ContextsProbe(reference: Remote(bound, other), page: PageOf(51, SentinelEntries(51))),
            () => ContextsProbe(nodeId: Different, reference: Remote(bound), page: PageOf(3, good)),
            () => ContextsProbe(reference: Remote(bound), page: new GitHubCheckContextsPage(1, null!)),
            // Duplicate entry identifiers.
            () => ContextsProbe(reference: Remote(bound), page: PageOf(2, RunEntry("CR_1", Sentinel), RunEntry("CR_1", Sentinel + "2"))),
            () => ContextsProbe(reference: Remote(bound, other), page: PageOf(2, RunEntry("CR_1", Sentinel), StatusEntry("CR_1", Sentinel))),
            () => ContextsProbe(nodeId: Different, reference: Remote(bound), page: PageOf(2, RunEntry("CR_1", Sentinel), RunEntry("CR_1", Sentinel))),
            // Invalid entry shapes and literals, in a page that would otherwise be withheld or reported.
            () => ContextsProbe(reference: Remote(bound), page: PageOf(1, RunEntry(name: Sentinel, conclusion: "success"))),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(1, RunEntry(name: Sentinel, status: "SUCCESS"))),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(1, StatusEntry(name: Sentinel, state: "QUEUED"))),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(1, RunEntry(name: Sentinel + "\n"))),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(1, RunEntry(name: "   "))),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(1, RunEntry(id: "bad id", name: Sentinel))),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(1, RunEntry(name: Sentinel, suite: "bad id"))),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(1, RunEntry(name: Sentinel) with { CheckSuiteId = null })),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(1, StatusEntry(name: Sentinel) with { Conclusion = "SUCCESS" })),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(1, StatusEntry(name: Sentinel) with { CheckSuiteId = "CS_1" })),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(1, RunEntry(name: Sentinel) with { Kind = "Other" })),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(2, RunEntry("CR_1", Sentinel), null!)),
            () => ContextsProbe(reference: Remote(bound, other), page: PageOf(1, RunEntry(name: Sentinel, conclusion: "success"))),
            () => ContextsProbe(reference: Remote(bound, other), page: PageOf(1, RunEntry(name: Sentinel + "\n"))),
            () => ContextsProbe(nodeId: Different, reference: Remote(bound), page: PageOf(1, RunEntry(name: Sentinel, status: "SUCCESS"))),
            () => ContextsProbe(nodeId: Different, reference: Remote(bound), page: PageOf(1, RunEntry(name: Sentinel + "\n"))),
            () => ContextsProbe(reference: Remote(bound), page: PageOf(51, [.. SentinelEntries(49), RunEntry("CR_49", Sentinel, conclusion: "success")])),
            () => ContextsProbe(reference: Remote(bound, other), page: PageOf(51, [.. SentinelEntries(49), RunEntry("CR_49", Sentinel + "\n")])),
        ];

        for (var index = 0; index < answers.Length; index++)
        {
            bound.Contexts.Answer = answers[index];

            var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

            Assert.True(reading.Status == HttpStatusCode.OK, $"case {index}");
            Assert.True(StatusOf(reading) == "Unknown", $"case {index}: {reading.Body}");
            AssertFacts(reading, source: true, target: true, binding: true, observation: false, $"case {index}");
            Assert.DoesNotContain(Sentinel, reading.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(Different, reading.Body, StringComparison.Ordinal);
        }

        Assert.Equal(answers.Length, bound.Contexts.Calls.Count);
    }

    [Fact]
    public async Task A_label_is_returned_as_inert_json_text_exactly_as_admitted_including_quotes_backslashes_markup_and_unicode()
    {
        var bound = await SnapshotBoundAsync();
        var labels = new[]
        {
            "say \"hi\" and back\\slash",
            "<script>alert(1)</script> [x](https://evil.example) &amp;",
            "ol" + char.ConvertFromUtf32(0xE1) + " " + char.ConvertFromUtf32(0x65E5) + char.ConvertFromUtf32(0x672C) + " " + char.ConvertFromUtf32(0x1F600),
            " padded ",
            "a/b",
        };
        var entries = labels.Select((label, index) => RunEntry($"CR_{index}", label)).ToArray();
        bound.Contexts.Answer = () => ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound), page: PageOf(entries.Length, entries));
        var started = DateTimeOffset.UtcNow;

        var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

        Assert.True(StatusOf(reading) == "ContextsReported", reading.Body);
        var observation = await AssertObservationAsync(reading, bound, started, Scene.BranchName, bound.Sha, labels.Length, contexts: true, "labels");
        AssertContexts(observation, entries);
        Assert.Equal(labels, observation.GetProperty("contexts").EnumerateArray().Select(entry => entry.GetProperty("name").GetString()!));
    }

    [Fact]
    public async Task A_clock_reading_that_precedes_the_binding_is_unknown_without_an_observation_and_an_equal_reading_is_admitted()
    {
        var bound = await SnapshotBoundAsync();
        var recordedAt = await BindingRecordedAtAsync(bound.Ids.RunId);
        var before = await DatabaseFingerprintAsync();

        bound.Contexts.Answer = () =>
        {
            bound.Clock.Fix(recordedAt.AddTicks(-1));
            return ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound), page: PageOf(1, SentinelEntries(1)));
        };
        var earlier = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);
        bound.Clock.Release();

        Assert.Equal(HttpStatusCode.OK, earlier.Status);
        Assert.True(StatusOf(earlier) == "Unknown", earlier.Body);
        AssertFacts(earlier, source: true, target: true, binding: true, observation: false);
        Assert.DoesNotContain(Sentinel, earlier.Body, StringComparison.Ordinal);
        Assert.Single(bound.Contexts.Calls);

        bound.Contexts.Answer = () =>
        {
            bound.Clock.Fix(recordedAt);
            return ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound), page: PageOf(1, SentinelEntries(1)));
        };
        var equal = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);
        bound.Clock.Release();

        Assert.True(StatusOf(equal) == "ContextsReported", equal.Body);
        Assert.Equal(recordedAt, equal.Root.GetProperty("observation").GetProperty("observedAtUtc").GetDateTimeOffset());
        Assert.Equal(2, bound.Contexts.Calls.Count);
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task An_identity_change_carries_no_observation_time_and_does_not_depend_on_the_clock_precedence_rule()
    {
        var bound = await SnapshotBoundAsync();
        var recordedAt = await BindingRecordedAtAsync(bound.Ids.RunId);
        bound.Contexts.Answer = () =>
        {
            bound.Clock.Fix(recordedAt.AddDays(-1));
            return ContextsProbe(nodeId: "R_kgDODifferent9");
        };

        var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);
        bound.Clock.Release();

        Assert.True(StatusOf(reading) == "IdentityChanged", reading.Body);
        AssertFacts(reading, source: true, target: true, binding: true, observation: false);
    }

    [Fact]
    public async Task Each_request_observes_once_more_and_never_reuses_an_earlier_answer_or_a_sibling_observation()
    {
        var bound = await SnapshotBoundAsync();
        bound.Contexts.Answer = () => ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound), page: PageOf(1, RunEntry(name: "first")));

        var first = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);
        bound.Contexts.Answer = () => ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound), page: PageOf(1, RunEntry(name: "second")));
        var second = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);
        bound.Contexts.Answer = () => Task.FromResult<GitHubCheckContextsProbe?>(null);
        var third = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal("first", first.Root.GetProperty("observation").GetProperty("contexts")[0].GetProperty("name").GetString());
        Assert.Equal("second", second.Root.GetProperty("observation").GetProperty("contexts")[0].GetProperty("name").GetString());
        Assert.Equal("Unknown", StatusOf(third));
        Assert.DoesNotContain("second", third.Body, StringComparison.Ordinal);
        Assert.Equal(3, bound.Contexts.Calls.Count);
        Assert.Empty(bound.IdentityObserver.Calls);
    }

    [Fact]
    public async Task The_observation_time_is_stamped_after_the_external_work_and_the_callers_cancellation_reaches_the_observer()
    {
        var bound = await SnapshotBoundAsync();
        bound.Contexts.Answer = async () =>
        {
            await Task.Delay(1_200);
            return new GitHubCheckContextsProbe(NodeId, DefaultOwner, DefaultRepository, null, null);
        };

        var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal("RefNotReturned", StatusOf(reading));
        var observedAt = reading.Root.GetProperty("observation").GetProperty("observedAtUtc").GetDateTimeOffset();
        Assert.True(observedAt >= bound.Contexts.LastStartedAt!.Value.AddMilliseconds(1_100), $"{observedAt:o} vs {bound.Contexts.LastStartedAt:o}");
        Assert.True(Assert.Single(bound.Contexts.Tokens).CanBeCanceled);
    }

    [Fact]
    public async Task An_unexpected_observer_fault_is_not_converted_into_a_status_and_writes_nothing()
    {
        var bound = await SnapshotBoundAsync();
        bound.Contexts.Answer = () => throw new InvalidOperationException("boom");
        var before = await DatabaseFingerprintAsync();

        var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.InternalServerError, reading.Status);
        Assert.Equal(before, await DatabaseFingerprintAsync());
        Assert.Single(bound.Contexts.Calls);
    }

    [Fact]
    public async Task A_query_string_is_ignored_and_the_observer_receives_only_the_recorded_scope()
    {
        var bound = await SnapshotBoundAsync();
        bound.Contexts.Answer = () => ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound), page: PageOf(1, RunEntry()));

        var reading = await ObserveContextsAsync(
            bound.Host, bound.Ids.RunId, query: "?owner=sentinel-owner&ref=main&sha=" + new string('f', 40) + "&first=1&after=xyz&limit=5");

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal("ContextsReported", StatusOf(reading));
        AssertCall(bound, 1);
        Assert.DoesNotContain("sentinel-owner", reading.Body, StringComparison.Ordinal);
    }

    private sealed class ScriptedProcessAdapter(Func<string> standardOutput) : IProcessExecutionAdapter
    {
        public int Invocations { get; private set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Invocations++;
            return Task.FromResult(new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = standardOutput(),
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromMilliseconds(5),
            });
        }
    }

    /// <summary>The five supported secret-shape classes, built from fragments so no complete token-shaped value exists in this source and
    /// no assertion message prints one. All fictitious.</summary>
    private static readonly string[] SecretShapes =
    [
        "Bearer " + "A1b2C3d4E5f6",
        "github" + "_pat_" + new string('b', 30),
        "xox" + "b-" + "1234567890ab",
        "gh" + "p_" + new string('c', 36),
        "AK" + "IA" + "ABCDEFGHIJKLMNOP",
    ];

    private static string EscapeFirst(string value) => "\\" + "u" + ((int)value[0]).ToString("x4") + value[1..];

    /// <summary>
    /// Representative safe projection (ADR-0045): the REAL adapter (the production observer) on the production host, with only the
    /// process engine scripted to print a matching answer whose labels carry the five supported secret-shape classes with the first
    /// character written as a JSON escape (which the executor's raw-text redaction cannot see). Every label reaches the wire only as the
    /// safe, redacted text; nothing of any shape appears in the response, and the answer is otherwise the normal ContextsReported one.
    /// </summary>
    [Fact]
    public async Task A_secret_shaped_label_decoded_from_an_escape_reaches_the_wire_only_as_redacted_text_through_the_real_adapter()
    {
        var sha = string.Empty;
        var process = new ScriptedProcessAdapter(() =>
        {
            var nodes = new List<string>();
            for (var index = 0; index < SecretShapes.Length; index++)
            {
                nodes.Add(
                    "{\"__typename\":\"CheckRun\",\"id\":\"CR_" + index + "\",\"name\":\"run " + EscapeFirst(SecretShapes[index])
                    + " end\",\"status\":\"COMPLETED\",\"conclusion\":\"SUCCESS\",\"checkSuite\":{\"id\":\"CS_1\"}}");
                nodes.Add(
                    "{\"__typename\":\"StatusContext\",\"id\":\"SC_" + index + "\",\"context\":\"" + EscapeFirst(SecretShapes[index])
                    + "\",\"state\":\"PENDING\"}");
            }

            return "{\"data\":{\"repository\":{\"id\":\"" + NodeId + "\",\"name\":\"" + DefaultRepository + "\",\"nameWithOwner\":\""
                + DefaultOwner + "/" + DefaultRepository + "\",\"ref\":{\"name\":\"" + Scene.BranchName
                + "\",\"prefix\":\"refs/heads/\",\"target\":{\"__typename\":\"Commit\",\"oid\":\"" + sha
                + "\",\"statusCheckRollup\":{\"contexts\":{\"totalCount\":" + nodes.Count
                + ",\"pageInfo\":{\"hasNextPage\":false},\"nodes\":[" + string.Join(",", nodes) + "]}}}}}}}";
        });
        var bound = await SnapshotBoundAsync(observer: new GitHubCheckContextsObservationAdapter(process));
        sha = bound.Sha;
        var before = await DatabaseFingerprintAsync();

        var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.True(StatusOf(reading) == "ContextsReported", "the answer was not the normal ContextsReported one");
        Assert.Equal(1, process.Invocations);
        var names = reading.Root.GetProperty("observation").GetProperty("contexts").EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(SecretShapes.Length * 2, names.Length);
        Assert.True(
            names.Where((_, index) => index % 2 == 0).All(name => name == "run [REDACTED] end")
            && names.Where((_, index) => index % 2 == 1).All(name => name == "[REDACTED]"),
            "a label did not reach the wire as exactly the redacted safe text");
        Assert.True(
            SecretShapes.All(shape => !reading.Body.Contains(shape, StringComparison.Ordinal)
                && !reading.Body.Contains(shape[1..], StringComparison.Ordinal)),
            "the response carries a supported secret shape");
        Assert.Equal(before, await DatabaseFingerprintAsync());
    }

    [Fact]
    public async Task The_response_never_contains_a_translated_aggregate_or_an_approval_word_for_any_successful_looking_collection()
    {
        var bound = await SnapshotBoundAsync();
        var entries = new[] { RunEntry("CR_1", "tests", "COMPLETED", "SUCCESS"), StatusEntry("SC_1", "ci", "SUCCESS") };
        bound.Contexts.Answer = () => ContextsProbe("OCTO-CAT", "Hello.World", reference: Remote(bound), page: PageOf(2, entries));

        var reading = await ObserveContextsAsync(bound.Host, bound.Ids.RunId);

        Assert.Equal("ContextsReported", StatusOf(reading));
        AssertNoTranslatedWords(reading);
        var observation = reading.Root.GetProperty("observation");
        Assert.DoesNotContain(observation.EnumerateObject(), property => property.Name is "rollupState" or "state" or "verdict" or "success");
        var bytes = Encoding.UTF8.GetByteCount(reading.Body);
        Assert.InRange(bytes, 100, 4_000);
    }
}
