using System.Net;
using System.Text.Json;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0040: a selection is the recorded one or it is unavailable. Each case breaks exactly one recorded fact of a real, completed
/// delivery (the source before anything is recorded, or the target and its event after), proves the read is Unavailable (or has no
/// admissible source) and that a write is refused and never replayed as a success, then restores every row and trigger and proves the
/// original answer returns byte for byte, so no case can pass on damage left behind by an earlier one.
/// </summary>
public sealed class GitHubObservationTargetCorruptionTests : GitHubObservationTargetTestBase
{
    private sealed record Case(string Name, string[] Tables, string[] Statements);

    private const string Operations = "local_commit_operations";
    private const string Workspaces = "git_workspaces";
    private const string Runs = "runs";
    private const string Members = "local_commit_authority_members";
    private const string Targets = "github_observation_targets";
    private const string Events = "events";

    [Fact]
    public async Task A_source_that_is_not_admissible_has_no_source_and_refuses_a_selection_then_returns_when_restored()
    {
        LocalCommitLineageIds ids;
        Guid operationId;
        using (var delivering = StartHost(runSupervisor: true))
        {
            (ids, operationId) = await DeliverAsync(delivering);
        }

        var sha = await CommitShaOfAsync(operationId);
        using var host = StartHost(runSupervisor: false);
        var body = BodyOf(operationId, sha);
        var goodRead = (await GetTargetAsync(host, ids.RunId)).Body;
        Assert.Equal("NotRecorded", System.Text.Json.JsonDocument.Parse(goodRead).RootElement.GetProperty("state").GetString());
        var operation = Upper(operationId);
        var workspace = Upper(ids.WorkspaceId);
        var someone = Upper(Guid.NewGuid());

        Case[] cases =
        [
            new("operation commit sha is malformed", [Operations], [$"UPDATE {Operations} SET \"CommitSha\" = 'not-a-sha' WHERE \"Id\" = '{operation}'"]),
            new("completed operation has no completion time", [Operations], [$"UPDATE {Operations} SET \"CompletedAtUtc\" = NULL WHERE \"Id\" = '{operation}'"]),
            new("operation names another workspace branch", [Operations], [$"UPDATE {Operations} SET \"BranchName\" = 'elsewhere' WHERE \"Id\" = '{operation}'"]),
            new("operation belongs to another project", [Operations], [$"UPDATE {Operations} SET \"ProjectId\" = '{someone}' WHERE \"Id\" = '{operation}'"]),
            new("workspace branch is not the recorded branch", [Workspaces], [$"UPDATE {Workspaces} SET \"BranchName\" = 'elsewhere' WHERE \"Id\" = '{workspace}'"]),
            new("workspace belongs to another project", [Workspaces], [$"UPDATE {Workspaces} SET \"ProjectId\" = '{someone}' WHERE \"Id\" = '{workspace}'"]),
            new("workspace number no longer reproduces the recorded branch", [Workspaces], [$"UPDATE {Workspaces} SET \"WorkspaceNumber\" = 2 WHERE \"Id\" = '{workspace}'"]),
            new("workspace number is not positive", [Workspaces], [$"UPDATE {Workspaces} SET \"WorkspaceNumber\" = 0 WHERE \"Id\" = '{workspace}'"]),
            new("recorded digest of the human decision is wrong", [Members], [$"UPDATE {Members} SET \"Digest\" = '{Zeros64}' WHERE \"OperationId\" = '{operation}' AND \"Kind\" = 'HumanReview'"]),
            new("run is no longer completed", [Runs], [$"UPDATE {Runs} SET \"Lifecycle\" = 'Running' WHERE \"Id\" = '{Upper(ids.RunId)}'"]),
            new("run execution mode is malformed", [Runs], [$"UPDATE {Runs} SET \"ExecutionMode\" = 'garbage' WHERE \"Id\" = '{Upper(ids.RunId)}'"]),
            new("run execution mode is a different recognized mode", [Runs], [$"UPDATE {Runs} SET \"ExecutionMode\" = 0 WHERE \"Id\" = '{Upper(ids.RunId)}'"]),
            new("run execution mode is stored as a real", [Runs], [$"UPDATE {Runs} SET \"ExecutionMode\" = 1.0 WHERE \"Id\" = '{Upper(ids.RunId)}'"]),
            new("run execution mode is a blob", [Runs], [$"UPDATE {Runs} SET \"ExecutionMode\" = x'01' WHERE \"Id\" = '{Upper(ids.RunId)}'"]),
        ];

        foreach (var broken in cases)
        {
            await CorruptAsync(broken.Tables, broken.Statements, async () =>
            {
                var fingerprint = await DatabaseFingerprintAsync();

                var read = await GetTargetAsync(host, ids.RunId);
                Assert.True(read.Status == HttpStatusCode.OK, broken.Name);
                Assert.True(read.State == "NotRecorded", broken.Name);
                Assert.True(read.Root.GetProperty("source").ValueKind == JsonValueKind.Null, broken.Name);
                Assert.True(read.Root.GetProperty("target").ValueKind == JsonValueKind.Null, broken.Name);

                var write = await PostTargetAsync(host, ids.RunId, body);
                Assert.True(write.Status == HttpStatusCode.Conflict, $"{broken.Name}: {write.Status} {write.Body}");
                Assert.True(
                    write.Root.GetProperty("errors")[0].GetProperty("code").GetString() == "github_observation_target.unavailable", broken.Name);
                Assert.True((await CountsAsync(ids.RunId)) == (0, 0), broken.Name);
                Assert.True(fingerprint == await DatabaseFingerprintAsync(), broken.Name);
            });

            Assert.True(goodRead == (await GetTargetAsync(host, ids.RunId)).Body, $"{broken.Name}: the original answer did not return");
        }

        Assert.Equal(HttpStatusCode.OK, (await PostTargetAsync(host, ids.RunId, body)).Status);
    }

    [Fact]
    public async Task A_recorded_target_or_event_that_is_not_coherent_is_unavailable_and_never_a_successful_replay_then_returns_when_restored()
    {
        LocalCommitLineageIds ids;
        Guid operationId;
        using (var delivering = StartHost(runSupervisor: true))
        {
            (ids, operationId) = await DeliverAsync(delivering);
        }

        var sha = await CommitShaOfAsync(operationId);
        using var host = StartHost(runSupervisor: false);
        var body = BodyOf(operationId, sha);
        var recorded = await RecordAsync(host, ids.RunId, operationId, sha);
        var goodRead = (await GetTargetAsync(host, ids.RunId)).Body;
        Assert.Equal("Available", JsonDocument.Parse(goodRead).RootElement.GetProperty("state").GetString());
        var row = await TargetRowAsync(ids.RunId);
        var target = Upper(row.Id);
        var eventId = Upper(row.EventId);
        var someone = Upper(Guid.NewGuid());
        var eventOfTarget = $"\"EventType\" = '{TargetEventType}' AND \"RunId\" = '{Upper(ids.RunId)}'";

        Case[] cases =
        [
            new("target owner is outside the supported subset", [Targets], [$"UPDATE {Targets} SET \"Owner\" = 'bad owner' WHERE \"Id\" = '{target}'"]),
            new("target repository is outside the supported subset", [Targets], [$"UPDATE {Targets} SET \"Repository\" = '.bad' WHERE \"Id\" = '{target}'"]),
            new("target owner differs from the event payload", [Targets], [$"UPDATE {Targets} SET \"Owner\" = 'someone-else' WHERE \"Id\" = '{target}'"]),
            new("target commit is malformed", [Targets], [$"UPDATE {Targets} SET \"CommitSha\" = 'XYZ' WHERE \"Id\" = '{target}'"]),
            new("target commit is another well formed commit", [Targets], [$"UPDATE {Targets} SET \"CommitSha\" = '{Nines40}' WHERE \"Id\" = '{target}'"]),
            new("target branch is another branch", [Targets], [$"UPDATE {Targets} SET \"BranchName\" = 'elsewhere' WHERE \"Id\" = '{target}'"]),
            new("target project is foreign", [Targets], [$"UPDATE {Targets} SET \"ProjectId\" = '{someone}' WHERE \"Id\" = '{target}'"]),
            new("target workspace is foreign", [Targets], [$"UPDATE {Targets} SET \"GitWorkspaceId\" = '{someone}' WHERE \"Id\" = '{target}'"]),
            new("target belongs to another run", [Targets], [$"UPDATE {Targets} SET \"RunId\" = '{someone}' WHERE \"Id\" = '{target}'"]),
            new("target names another operation", [Targets], [$"UPDATE {Targets} SET \"LocalCommitOperationId\" = '{someone}' WHERE \"Id\" = '{target}'"]),
            new("target names another event", [Targets], [$"UPDATE {Targets} SET \"EventId\" = '{someone}' WHERE \"Id\" = '{target}'"]),
            new("target time differs from the event time", [Targets], [$"UPDATE {Targets} SET \"RecordedAtUtc\" = '2001-01-01 00:00:00+00:00' WHERE \"Id\" = '{target}'"]),
            new("target time has an offset", [Targets, Events], [$"UPDATE {Targets} SET \"RecordedAtUtc\" = '2001-01-01 01:00:00+01:00' WHERE \"Id\" = '{target}'", $"UPDATE {Events} SET \"OccurredAtUtc\" = '2001-01-01 01:00:00+01:00' WHERE {eventOfTarget}"]),
            new("event is missing", [Events], [$"DELETE FROM {Events} WHERE {eventOfTarget}"]),
            new("event has another identifier", [Events], [$"UPDATE {Events} SET \"Id\" = '{someone}' WHERE {eventOfTarget}"]),
            new("event belongs to another run", [Events], [$"UPDATE {Events} SET \"RunId\" = '{someone}' WHERE \"Id\" = '{eventId}'"]),
            new("event payload names another owner", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = replace(\"PayloadJson\", 'octo-cat', 'octo-dog') WHERE {eventOfTarget}"]),
            new("event payload is version two", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = replace(\"PayloadJson\", '\"version\":1', '\"version\":2') WHERE {eventOfTarget}"]),
            new("event payload has an extra member", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = replace(\"PayloadJson\", '}}', ',\"x\":1}}') WHERE {eventOfTarget}"]),
            new("event payload is over the bound", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = \"PayloadJson\" || '{new string(' ', 1100)}' WHERE {eventOfTarget}"]),
            new("event payload is empty", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = '' WHERE {eventOfTarget}"]),
            new("event was authored by an agent", [Events], [$"UPDATE {Events} SET \"ActorKind\" = 'Agent', \"ActorAgentRole\" = 'Planner', \"ActorAgentProvider\" = 'Codex' WHERE {eventOfTarget}"]),
            new("event was authored by the orchestrator", [Events], [$"UPDATE {Events} SET \"ActorKind\" = 'Orchestrator' WHERE {eventOfTarget}"]),
            new("event actor kind is an unknown stored value", [Events], [$"UPDATE {Events} SET \"ActorKind\" = 'UnrecognizedFutureValue' WHERE {eventOfTarget}"]),
            new("event actor role is an unknown stored value", [Events], [$"UPDATE {Events} SET \"ActorAgentRole\" = 'UnrecognizedFutureValue' WHERE {eventOfTarget}"]),
            new("event actor provider is an unknown stored value", [Events], [$"UPDATE {Events} SET \"ActorAgentProvider\" = 'UnrecognizedFutureValue' WHERE {eventOfTarget}"]),
            new("event payload hides a large suffix behind an embedded NUL", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = \"PayloadJson\" || char(0) || replace(hex(zeroblob(50000)), '0', 'x') WHERE {eventOfTarget}"]),
            new("target owner is oversized", [Targets], [$"UPDATE {Targets} SET \"Owner\" = replace(hex(zeroblob(150)), '0', 'o') WHERE \"Id\" = '{target}'"]),
            new("target repository is oversized", [Targets], [$"UPDATE {Targets} SET \"Repository\" = replace(hex(zeroblob(150)), '0', 'r') WHERE \"Id\" = '{target}'"]),
            new("target branch is oversized", [Targets], [$"UPDATE {Targets} SET \"BranchName\" = replace(hex(zeroblob(2000)), '0', 'b') WHERE \"Id\" = '{target}'"]),
            new("target commit is oversized", [Targets], [$"UPDATE {Targets} SET \"CommitSha\" = replace(hex(zeroblob(150)), '0', 'a') WHERE \"Id\" = '{target}'"]),
            new("target owner hides a large suffix behind an embedded NUL", [Targets], [$"UPDATE {Targets} SET \"Owner\" = 'ab' || char(0) || replace(hex(zeroblob(50000)), '0', 'x') WHERE \"Id\" = '{target}'"]),
            new("event names an attempt", [Events], [$"UPDATE {Events} SET \"AttemptId\" = '{Upper(ids.ReviewAttemptId)}' WHERE {eventOfTarget}"]),
            new("event time differs from the target time", [Events], [$"UPDATE {Events} SET \"OccurredAtUtc\" = '2001-01-01 00:00:00+00:00' WHERE {eventOfTarget}"]),
            new("run owns a second event of the recorded type", [Events],
            [
                $"INSERT INTO {Events} (\"Id\", \"RunId\", \"AttemptId\", \"EventType\", \"ActorKind\", \"ActorAgentRole\", \"ActorAgentProvider\", \"PayloadJson\", \"OccurredAtUtc\") "
                + $"SELECT '{someone}', \"RunId\", \"AttemptId\", \"EventType\", \"ActorKind\", \"ActorAgentRole\", \"ActorAgentProvider\", \"PayloadJson\", \"OccurredAtUtc\" FROM {Events} WHERE {eventOfTarget}",
            ]),
            new("source commit no longer matches the target", [Operations], [$"UPDATE {Operations} SET \"CommitSha\" = '{Nines40}' WHERE \"Id\" = '{Upper(operationId)}'"]),
            new("source workspace number no longer reproduces the branch", [Workspaces], [$"UPDATE {Workspaces} SET \"WorkspaceNumber\" = 2 WHERE \"Id\" = '{Upper(ids.WorkspaceId)}'"]),
        ];

        foreach (var broken in cases)
        {
            await CorruptAsync(broken.Tables, broken.Statements, async () =>
            {
                var fingerprint = await DatabaseFingerprintAsync();

                var read = await GetTargetAsync(host, ids.RunId);
                Assert.True(read.Status == HttpStatusCode.OK, $"{broken.Name}: {read.Status} {read.Body}");
                Assert.True(read.State == "Unavailable", $"{broken.Name}: {read.Body}");
                Assert.True(read.Root.GetProperty("source").ValueKind == JsonValueKind.Null, broken.Name);
                Assert.True(read.Root.GetProperty("target").ValueKind == JsonValueKind.Null, broken.Name);

                var write = await PostTargetAsync(host, ids.RunId, body);
                Assert.True(write.Status == HttpStatusCode.Conflict, $"{broken.Name}: {write.Status} {write.Body}");
                Assert.True(
                    write.Root.GetProperty("errors")[0].GetProperty("code").GetString() == "github_observation_target.unavailable", broken.Name);
                Assert.True(fingerprint == await DatabaseFingerprintAsync(), broken.Name);
            });

            Assert.True(goodRead == (await GetTargetAsync(host, ids.RunId)).Body, $"{broken.Name}: the original answer did not return");
            Assert.True(recorded == (await PostTargetAsync(host, ids.RunId, body)).Body, $"{broken.Name}: the original replay did not return");
        }
    }
}
