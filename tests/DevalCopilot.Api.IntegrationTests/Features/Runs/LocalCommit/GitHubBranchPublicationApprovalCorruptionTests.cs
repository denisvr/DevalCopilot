using System.Net;
using System.Text.Json;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0048: an approval is the recorded one or it is unavailable. Each case breaks exactly one recorded fact of a real approval, its
/// event, its binding, its target or an unrelated stored value it must withhold, proves the read is Unavailable (never NotRecorded or
/// ApprovalNotRecorded, with every fact null) and a repeated write is refused and never replayed as a success, then restores every row and
/// trigger and proves the original answers return byte for byte.
/// </summary>
public sealed class GitHubBranchPublicationApprovalCorruptionTests : GitHubBranchPublicationApprovalTestBase
{
    private sealed record Case(string Name, string[] Tables, string[] Statements);

    private const string Bindings = "github_repository_identity_bindings";
    private const string Targets = "github_observation_targets";
    private const string Events = "events";

    [Fact]
    public async Task An_approval_event_binding_or_target_that_is_not_coherent_is_unavailable_and_never_a_successful_replay_then_returns_when_restored()
    {
        var bound = await BoundAsync();
        var body = ApprovalBodyFor(bound);
        var recorded = await PostApprovalAsync(bound.Host, bound.RunId, body);
        Assert.Equal(HttpStatusCode.OK, recorded.Status);
        var goodRead = (await GetApprovalAsync(bound.Host, bound.RunId)).Body;
        var row = await ApprovalRowAsync(bound.RunId);
        var approval = Upper(row.Id);
        var eventId = Upper(row.EventId);
        var someone = Upper(Guid.NewGuid());
        var run = Upper(bound.RunId);
        var ownEvent = $"\"EventType\" = '{ApprovalEventType}' AND \"RunId\" = '{run}'";
        var bindingEvent = $"\"EventType\" = '{BindingEventType}' AND \"RunId\" = '{run}'";
        var targetEvent = $"\"EventType\" = '{TargetEventType}' AND \"RunId\" = '{run}'";
        string Row(string assignments) => $"UPDATE {Approvals} SET {assignments} WHERE \"Id\" = '{approval}'";

        Case[] cases =
        [
            new("node identifier differs from the binding", [Approvals], [Row("\"RepositoryNodeId\" = 'R_other'")]),
            new("commit is another well formed commit", [Approvals], [Row($"\"CommitSha\" = '{Nines40}'")]),
            new("branch is another branch", [Approvals], [Row("\"BranchName\" = 'elsewhere'")]),
            new("target names differ in case from the target", [Approvals], [Row("\"TargetOwner\" = 'OCTO-CAT'")]),
            new("binding names differ in case from the binding", [Approvals], [Row("\"BindingRepository\" = 'HELLO.WORLD'")]),
            new("binding observation time differs", [Approvals], [Row("\"BindingObservedAtUtc\" = '2999-01-01 00:00:00+00:00'")]),
            new("target event is another", [Approvals], [Row($"\"TargetEventId\" = '{someone}'")]),
            new("binding event is another", [Approvals], [Row($"\"BindingEventId\" = '{someone}'")]),
            new("workspace is foreign", [Approvals], [Row($"\"GitWorkspaceId\" = '{someone}'")]),
            new("approval belongs to another run", [Approvals], [Row($"\"RunId\" = '{someone}'")]),
            new("approved before the binding was recorded", [Approvals, Events],
            [
                Row("\"ApprovedAtUtc\" = '2001-01-01 00:00:00+00:00'"),
                $"UPDATE {Events} SET \"OccurredAtUtc\" = '2001-01-01 00:00:00+00:00' WHERE {ownEvent}",
            ]),
            new("approval time differs from the event time", [Approvals], [Row("\"ApprovedAtUtc\" = '2999-01-01 00:00:00+00:00'")]),
            new("approval event is missing", [Events], [$"DELETE FROM {Events} WHERE {ownEvent}"]),
            new("approval row is missing and its event is an orphan", [Approvals], [$"DELETE FROM {Approvals} WHERE \"Id\" = '{approval}'"]),
            new("event belongs to another run", [Events], [$"UPDATE {Events} SET \"RunId\" = '{someone}' WHERE \"Id\" = '{eventId}'"]),
            new("event payload names another node identifier", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = replace(\"PayloadJson\", '{NodeId}', 'R_other') WHERE {ownEvent}"]),
            new("event payload is version two", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = replace(\"PayloadJson\", '\"version\":1', '\"version\":2') WHERE {ownEvent}"]),
            new("event payload names another action", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = replace(\"PayloadJson\", 'PushExactCommit', 'ForcePush') WHERE {ownEvent}"]),
            new("event payload is over the bound", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = \"PayloadJson\" || '{new string(' ', 4200)}' WHERE {ownEvent}"]),
            new("event was authored by an agent", [Events], [$"UPDATE {Events} SET \"ActorKind\" = 'Agent', \"ActorAgentRole\" = 'Planner', \"ActorAgentProvider\" = 'Codex' WHERE {ownEvent}"]),
            new("event actor kind is an unknown stored value", [Events], [$"UPDATE {Events} SET \"ActorKind\" = 'UnrecognizedFutureValue' WHERE {ownEvent}"]),
            new("event names an attempt", [Events], [$"UPDATE {Events} SET \"AttemptId\" = '{Upper(bound.Delivered.Ids.ReviewAttemptId)}' WHERE {ownEvent}"]),
            new("event payload hides a large suffix behind an embedded NUL", [Events], [$"UPDATE {Events} SET \"PayloadJson\" = \"PayloadJson\" || char(0) || replace(hex(zeroblob(50000)), '0', 'x') WHERE {ownEvent}"]),
            new("node identifier hides a large suffix behind an embedded NUL", [Approvals], [Row("\"RepositoryNodeId\" = 'ab' || char(0) || replace(hex(zeroblob(50000)), '0', 'x')")]),
            new("binding is missing and the approval is an orphan", [Bindings], [$"DELETE FROM {Bindings} WHERE \"Id\" = '{Upper(bound.BindingId)}'"]),
            new("binding event is missing", [Events], [$"DELETE FROM {Events} WHERE {bindingEvent}"]),
            new("binding node identifier changed with its event payload left behind", [Bindings], [$"UPDATE {Bindings} SET \"RepositoryNodeId\" = 'R_other' WHERE \"Id\" = '{Upper(bound.BindingId)}'"]),
            new("target is missing and the approval is an orphan", [Targets], [$"DELETE FROM {Targets} WHERE \"Id\" = '{Upper(bound.Delivered.TargetId)}'"]),
            new("target event is missing", [Events], [$"DELETE FROM {Events} WHERE {targetEvent}"]),
            new("run owns a second approval event", [Events],
            [
                $"INSERT INTO {Events} (\"Id\", \"RunId\", \"AttemptId\", \"EventType\", \"ActorKind\", \"ActorAgentRole\", \"ActorAgentProvider\", \"PayloadJson\", \"OccurredAtUtc\") "
                + $"SELECT '{someone}', \"RunId\", \"AttemptId\", \"EventType\", \"ActorKind\", \"ActorAgentRole\", \"ActorAgentProvider\", \"PayloadJson\", \"OccurredAtUtc\" FROM {Events} WHERE {ownEvent}",
            ]),
        ];

        foreach (var broken in cases)
        {
            await CorruptAsync(broken.Tables, broken.Statements, async () =>
            {
                var fingerprint = await DatabaseFingerprintAsync();

                var read = await GetApprovalAsync(bound.Host, bound.RunId);
                Assert.True(read.Status == HttpStatusCode.OK, $"{broken.Name}: {read.Status} {read.Body}");
                Assert.True(StatusOf(read) == "Unavailable", $"{broken.Name}: {read.Body}");
                foreach (var member in new[] { "source", "target", "binding", "approval" })
                {
                    Assert.True(read.Root.GetProperty(member).ValueKind == JsonValueKind.Null, $"{broken.Name}: {member}");
                }

                var write = await PostApprovalAsync(bound.Host, bound.RunId, body);
                Assert.True(write.Status == HttpStatusCode.Conflict, $"{broken.Name}: {write.Status} {write.Body}");
                Assert.True(ErrorCode(write) == "github_branch_publication_approval.unavailable", $"{broken.Name}: {write.Body}");
                Assert.True(fingerprint == await DatabaseFingerprintAsync(), broken.Name);
            });

            Assert.True(goodRead == (await GetApprovalAsync(bound.Host, bound.RunId)).Body, $"{broken.Name}: the answer did not return");
            Assert.True(recorded.Body == (await PostApprovalAsync(bound.Host, bound.RunId, body)).Body, $"{broken.Name}: replay did not return");
        }

        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    // ADR-0048 correction R1: a stored identifier or time that is malformed, oversized, NUL-containing or in a representation the writer
    // never produces is withheld before it is parsed, so the host answers Unavailable and refuses the replay instead of faulting in EF.
    [Fact]
    public async Task A_malformed_oversized_nul_containing_or_unsupported_stored_identifier_or_time_is_unavailable_never_a_fault_then_returns_when_restored()
    {
        var bound = await BoundAsync();
        var body = ApprovalBodyFor(bound);
        var recorded = await PostApprovalAsync(bound.Host, bound.RunId, body);
        Assert.Equal(HttpStatusCode.OK, recorded.Status);
        var goodRead = (await GetApprovalAsync(bound.Host, bound.RunId)).Body;
        var row = await ApprovalRowAsync(bound.RunId);
        var approval = Upper(row.Id);
        var ownEvent = $"\"EventType\" = '{ApprovalEventType}' AND \"RunId\" = '{Upper(bound.RunId)}'";
        string Row(string assignments) => $"UPDATE {Approvals} SET {assignments} WHERE \"Id\" = '{approval}'";
        string Event(string assignments) => $"UPDATE {Events} SET {assignments} WHERE {ownEvent}";
        var hugeSuffix = "replace(hex(zeroblob(30000)), '0', 'x')";

        Case[] cases =
        [
            new("approval project identifier is malformed", [Approvals], [Row("\"ProjectId\" = 'not-a-guid'")]),
            new("approval time is malformed", [Approvals], [Row("\"ApprovedAtUtc\" = 'not-a-time'")]),
            new("approval run identifier is malformed", [Approvals], [Row("\"RunId\" = 'not-a-guid'")]),
            new("approval event identifier is oversized", [Approvals], [Row($"\"EventId\" = \"EventId\" || {hugeSuffix}")]),
            new("approval operation identifier hides a suffix behind an embedded NUL",
                [Approvals], [Row("\"LocalCommitOperationId\" = \"LocalCommitOperationId\" || char(0) || 'x'")]),
            new("approval binding identifier is padded with white space", [Approvals], [Row("\"BindingId\" = ' ' || \"BindingId\" || ' '")]),
            new("approval target time uses another separator", [Approvals],
                [Row("\"TargetRecordedAtUtc\" = replace(\"TargetRecordedAtUtc\", ' ', 'T')")]),
            new("approval binding recording time is oversized", [Approvals],
                [Row($"\"BindingRecordedAtUtc\" = \"BindingRecordedAtUtc\" || {hugeSuffix}")]),
            new("approval workspace identifier is a blob", [Approvals],
                [Row("\"GitWorkspaceId\" = x'00112233445566778899aabbccddeeff'")]),
            new("event identifier is malformed", [Events], [Event("\"Id\" = 'not-a-guid'")]),
            new("event time is malformed", [Events], [Event("\"OccurredAtUtc\" = 'not-a-time'")]),
            new("event time hides a suffix behind an embedded NUL", [Events],
                [Event("\"OccurredAtUtc\" = \"OccurredAtUtc\" || char(0) || 'x'")]),
        ];

        foreach (var broken in cases)
        {
            await CorruptAsync(broken.Tables, broken.Statements, async () =>
            {
                var fingerprint = await DatabaseFingerprintAsync();

                var read = await GetApprovalAsync(bound.Host, bound.RunId);
                Assert.True(read.Status == HttpStatusCode.OK, $"{broken.Name}: {read.Status}");
                Assert.True(StatusOf(read) == "Unavailable", $"{broken.Name}: {read.Body}");
                foreach (var member in new[] { "source", "target", "binding", "approval" })
                {
                    Assert.True(read.Root.GetProperty(member).ValueKind == JsonValueKind.Null, $"{broken.Name}: {member}");
                }

                var write = await PostApprovalAsync(bound.Host, bound.RunId, body);
                Assert.True(write.Status == HttpStatusCode.Conflict, $"{broken.Name}: {write.Status}");
                Assert.True(ErrorCode(write) == "github_branch_publication_approval.unavailable", $"{broken.Name}: {write.Body}");
                Assert.True(fingerprint == await DatabaseFingerprintAsync(), $"{broken.Name}: something was written or replaced");
            });

            Assert.True(goodRead == (await GetApprovalAsync(bound.Host, bound.RunId)).Body, $"{broken.Name}: the answer did not return");
            Assert.True(recorded.Body == (await PostApprovalAsync(bound.Host, bound.RunId, body)).Body, $"{broken.Name}: replay did not return");
        }

        Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
    }

    [Fact]
    public async Task An_incoherent_stored_approval_is_never_replaced_by_a_fresh_one()
    {
        var bound = await BoundAsync();
        await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound));
        var row = await ApprovalRowAsync(bound.RunId);

        await CorruptAsync(
            [Approvals],
            [$"UPDATE {Approvals} SET \"RepositoryNodeId\" = 'R_other' WHERE \"Id\" = '{Upper(row.Id)}'"],
            async () =>
            {
                var write = await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound));

                Assert.Equal(HttpStatusCode.Conflict, write.Status);
                Assert.Equal("github_branch_publication_approval.unavailable", ErrorCode(write));
                Assert.Equal((1, 1), await ApprovalCountsAsync(bound.RunId));
                await Task.CompletedTask;
            });
    }

    [Fact]
    public async Task A_stored_approval_event_without_its_row_keeps_the_next_approval_from_being_recorded_and_the_read_unavailable()
    {
        var bound = await BoundAsync();
        await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound));

        await CorruptAsync([Approvals], [$"DELETE FROM {Approvals}"], async () =>
        {
            var write = await PostApprovalAsync(bound.Host, bound.RunId, ApprovalBodyFor(bound));
            Assert.Equal(HttpStatusCode.Conflict, write.Status);
            Assert.Equal("Unavailable", StatusOf(await GetApprovalAsync(bound.Host, bound.RunId)));
            Assert.Equal((0, 1), await ApprovalCountsAsync(bound.RunId));
        });
    }
}
