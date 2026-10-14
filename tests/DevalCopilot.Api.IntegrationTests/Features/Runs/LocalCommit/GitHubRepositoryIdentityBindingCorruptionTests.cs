using System.Net;
using System.Text.Json;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0043: a binding is the recorded one or it is unavailable. Each case breaks exactly one recorded fact of a real binding, its target
/// or its event, proves the read is Unavailable (never NotRecorded) and a repeated write is refused and never replayed as a success
/// without observing, then restores every row and trigger and proves the original answer returns byte for byte.
/// </summary>
public sealed class GitHubRepositoryIdentityBindingCorruptionTests : GitHubRepositoryIdentityBindingTestBase
{
    private sealed record Case(string Name, string[] Tables, string[] Statements);

    private const string Bindings = "github_repository_identity_bindings";
    private const string Targets = "github_observation_targets";
    private const string Events = "events";

    [Fact]
    public async Task A_binding_target_or_event_that_is_not_coherent_is_unavailable_and_never_a_successful_replay_then_returns_when_restored()
    {
        var delivered = await DeliveredAsync();
        var body = BodyFor(delivered);
        var recorded = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, body);
        Assert.Equal(HttpStatusCode.OK, recorded.Status);
        var goodRead = (await GetBindingAsync(delivered.Host, delivered.Ids.RunId)).Body;
        var row = await BindingRowAsync(delivered.Ids.RunId);
        var binding = Upper(row.Id);
        var eventId = Upper(row.EventId);
        var target = Upper(delivered.TargetId);
        var someone = Upper(Guid.NewGuid());
        var eventOfBinding = $"\"EventType\" = '{BindingEventType}' AND \"RunId\" = '{Upper(delivered.Ids.RunId)}'";
        var eventOfTarget = $"\"EventType\" = '{TargetEventType}' AND \"RunId\" = '{Upper(delivered.Ids.RunId)}'";

        Case[] cases =
        [
            new("node identifier is outside the supported subset", [Bindings], [$"UPDATE {Bindings} SET \"RepositoryNodeId\" = 'has space' WHERE \"Id\" = '{binding}'"]),
            new("node identifier differs from the event payload", [Bindings], [$"UPDATE {Bindings} SET \"RepositoryNodeId\" = 'R_other' WHERE \"Id\" = '{binding}'"]),
            new("node identifier is empty", [Bindings], [$"UPDATE {Bindings} SET \"RepositoryNodeId\" = '' WHERE \"Id\" = '{binding}'"]),
            new("owner names another repository owner", [Bindings], [$"UPDATE {Bindings} SET \"Owner\" = 'someone-else' WHERE \"Id\" = '{binding}'"]),
            new("repository names another repository", [Bindings], [$"UPDATE {Bindings} SET \"Repository\" = 'renamed.repo' WHERE \"Id\" = '{binding}'"]),
            new("commit is another well formed commit", [Bindings], [$"UPDATE {Bindings} SET \"CommitSha\" = '{Nines40}' WHERE \"Id\" = '{binding}'"]),
            new("branch is another branch", [Bindings], [$"UPDATE {Bindings} SET \"BranchName\" = 'elsewhere' WHERE \"Id\" = '{binding}'"]),
            new("project is foreign", [Bindings], [$"UPDATE {Bindings} SET \"ProjectId\" = '{someone}' WHERE \"Id\" = '{binding}'"]),
            new("workspace is foreign", [Bindings], [$"UPDATE {Bindings} SET \"GitWorkspaceId\" = '{someone}' WHERE \"Id\" = '{binding}'"]),
            new("binding belongs to another run", [Bindings], [$"UPDATE {Bindings} SET \"RunId\" = '{someone}' WHERE \"Id\" = '{binding}'"]),
            new("binding names another operation", [Bindings], [$"UPDATE {Bindings} SET \"LocalCommitOperationId\" = '{someone}' WHERE \"Id\" = '{binding}'"]),
            new("binding names another target", [Bindings], [$"UPDATE {Bindings} SET \"ObservationTargetId\" = '{someone}' WHERE \"Id\" = '{binding}'"]),
            new("binding names another event", [Bindings], [$"UPDATE {Bindings} SET \"EventId\" = '{someone}' WHERE \"Id\" = '{binding}'"]),
            new("observation precedes the target", [Bindings], [$"UPDATE {Bindings} SET \"ObservedAtUtc\" = '2001-01-01 00:00:00+00:00' WHERE \"Id\" = '{binding}'"]),
            new("binding is recorded before it is observed", [Bindings, Events],
            [
                $"UPDATE {Bindings} SET \"RecordedAtUtc\" = '2001-01-01 00:00:00+00:00' WHERE \"Id\" = '{binding}'",
                $"UPDATE {Events} SET \"OccurredAtUtc\" = '2001-01-01 00:00:00+00:00' WHERE {eventOfBinding}",
            ]),
            new("binding time differs from the event time", [Bindings], [$"UPDATE {Bindings} SET \"RecordedAtUtc\" = '2999-01-01 00:00:00+00:00' WHERE \"Id\" = '{binding}'"]),
            new("event is missing", [Events], [$"DELETE FROM {Events} WHERE {eventOfBinding}"]),
            new("event has another identifier", [Events], [$"UPDATE {Events} SET \"Id\" = '{someone}' WHERE {eventOfBinding}"]),
            new("event belongs to another run", [Events], [$"UPDATE {Events} SET \"RunId\" = '{someone}' WHERE \"Id\" = '{eventId}'"]),
            new("event payload names another node identifier", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = replace(\"PayloadJson\", '{NodeId}', 'R_other') WHERE {eventOfBinding}"]),
            new("event payload is version two", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = replace(\"PayloadJson\", '\"version\":1', '\"version\":2') WHERE {eventOfBinding}"]),
            new("event payload has an extra member", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = replace(\"PayloadJson\", '}}', ',\"x\":1}}') WHERE {eventOfBinding}"]),
            new("event payload is over the bound", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = \"PayloadJson\" || '{new string(' ', 4200)}' WHERE {eventOfBinding}"]),
            new("event payload is empty", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = '' WHERE {eventOfBinding}"]),
            new("event was authored by an agent", [Events], [$"UPDATE {Events} SET \"ActorKind\" = 'Agent', \"ActorAgentRole\" = 'Planner', \"ActorAgentProvider\" = 'Codex' WHERE {eventOfBinding}"]),
            new("event actor kind is an unknown stored value", [Events], [$"UPDATE {Events} SET \"ActorKind\" = 'UnrecognizedFutureValue' WHERE {eventOfBinding}"]),
            new("event actor role is an unknown stored value", [Events], [$"UPDATE {Events} SET \"ActorAgentRole\" = 'UnrecognizedFutureValue' WHERE {eventOfBinding}"]),
            new("event actor provider is an unknown stored value", [Events], [$"UPDATE {Events} SET \"ActorAgentProvider\" = 'UnrecognizedFutureValue' WHERE {eventOfBinding}"]),
            new("event names an attempt", [Events], [$"UPDATE {Events} SET \"AttemptId\" = '{Upper(delivered.Ids.ReviewAttemptId)}' WHERE {eventOfBinding}"]),
            new("event payload hides a large suffix behind an embedded NUL", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = \"PayloadJson\" || char(0) || replace(hex(zeroblob(50000)), '0', 'x') WHERE {eventOfBinding}"]),
            new("node identifier is oversized", [Bindings], [$"UPDATE {Bindings} SET \"RepositoryNodeId\" = replace(hex(zeroblob(300)), '0', 'n') WHERE \"Id\" = '{binding}'"]),
            new("node identifier hides a large suffix behind an embedded NUL", [Bindings], [$"UPDATE {Bindings} SET \"RepositoryNodeId\" = 'ab' || char(0) || replace(hex(zeroblob(50000)), '0', 'x') WHERE \"Id\" = '{binding}'"]),
            new("owner is oversized", [Bindings], [$"UPDATE {Bindings} SET \"Owner\" = replace(hex(zeroblob(150)), '0', 'o') WHERE \"Id\" = '{binding}'"]),
            new("run owns a second event of the recorded type", [Events],
            [
                $"INSERT INTO {Events} (\"Id\", \"RunId\", \"AttemptId\", \"EventType\", \"ActorKind\", \"ActorAgentRole\", \"ActorAgentProvider\", \"PayloadJson\", \"OccurredAtUtc\") "
                + $"SELECT '{someone}', \"RunId\", \"AttemptId\", \"EventType\", \"ActorKind\", \"ActorAgentRole\", \"ActorAgentProvider\", \"PayloadJson\", \"OccurredAtUtc\" FROM {Events} WHERE {eventOfBinding}",
            ]),
            new("target is missing and the binding is an orphan", [Targets], [$"DELETE FROM {Targets} WHERE \"Id\" = '{target}'"]),
            new("target event is missing", [Events], [$"DELETE FROM {Events} WHERE {eventOfTarget}"]),
            new("target owner no longer matches the binding", [Targets], [$"UPDATE {Targets} SET \"Owner\" = 'someone-else' WHERE \"Id\" = '{target}'"]),
        ];

        foreach (var broken in cases)
        {
            await CorruptAsync(broken.Tables, broken.Statements, async () =>
            {
                var fingerprint = await DatabaseFingerprintAsync();

                var read = await GetBindingAsync(delivered.Host, delivered.Ids.RunId);
                Assert.True(read.Status == HttpStatusCode.OK, $"{broken.Name}: {read.Status} {read.Body}");
                Assert.True(read.Root.GetProperty("state").GetString() == "Unavailable", $"{broken.Name}: {read.Body}");
                foreach (var member in new[] { "source", "target", "binding" })
                {
                    Assert.True(read.Root.GetProperty(member).ValueKind == JsonValueKind.Null, $"{broken.Name}: {member}");
                }

                var write = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, body);
                Assert.True(write.Status == HttpStatusCode.Conflict, $"{broken.Name}: {write.Status} {write.Body}");
                Assert.True(ErrorCode(write) == "github_repository_identity_binding.unavailable", $"{broken.Name}: {write.Body}");
                Assert.True(fingerprint == await DatabaseFingerprintAsync(), broken.Name);
            });

            Assert.True(goodRead == (await GetBindingAsync(delivered.Host, delivered.Ids.RunId)).Body, $"{broken.Name}: the answer did not return");
            Assert.True(recorded.Body == (await PostBindingAsync(delivered.Host, delivered.Ids.RunId, body)).Body, $"{broken.Name}: replay did not return");
        }

        Assert.Single(delivered.Observer.Calls);
    }

    [Fact]
    public async Task An_incoherent_stored_binding_is_never_replaced_by_a_fresh_observation()
    {
        var delivered = await DeliveredAsync();
        var body = BodyFor(delivered);
        await PostBindingAsync(delivered.Host, delivered.Ids.RunId, body);
        var row = await BindingRowAsync(delivered.Ids.RunId);

        await CorruptAsync(
            [Bindings],
            [$"UPDATE {Bindings} SET \"Owner\" = 'someone-else' WHERE \"Id\" = '{Upper(row.Id)}'"],
            async () =>
            {
                var write = await PostBindingAsync(delivered.Host, delivered.Ids.RunId, body);

                Assert.Equal(HttpStatusCode.Conflict, write.Status);
                Assert.Single(delivered.Observer.Calls);
                Assert.Equal((1, 1), await BindingCountsAsync(delivered.Ids.RunId));
                await Task.CompletedTask;
            });
    }
}
