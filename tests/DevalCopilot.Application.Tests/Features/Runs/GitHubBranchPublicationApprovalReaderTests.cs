using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0048 reader on a real file-backed SQLite database with the production migrations: an approval is the recorded one or it is
/// incoherent. Nothing recorded is None; a coherent row and its one Human event read back exactly; every broken pinned value, cross-link,
/// time, actor, payload or count, an orphan row or event, a record of another run and a stored value that is hostile is incoherent (never
/// absent, never partially shown), and a scope that is not whole turns any record or event into an orphan.
/// </summary>
public sealed partial class GitHubBranchPublicationApprovalReaderTests : IAsyncLifetime
{
    private const string Approvals = "github_branch_publication_approvals";
    private const string Events = "events";
    private const string NodeId = "R_kgDOExample01";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed record Seeded(
        LocalCommitRows Rows,
        GitHubObservationTarget Target,
        GitHubRepositoryIdentityBinding Binding,
        GitHubBranchPublicationScopeReading Scope);

    private async Task<Seeded> SeedAsync()
    {
        await using var db = _fixture.CreateContext();
        var rows = await LocalCommitRowsSeed.SeedAsync(db, LocalCommitStatus.Completed, WorkspaceStatus.Ready);
        var target = GitHubObservationTarget.Record(
            Guid.NewGuid(), rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, rows.Operation.CommitSha,
            rows.Operation.BranchName, "octo-cat", "hello.world", LocalCommitRowsSeed.Now, Guid.NewGuid());
        var binding = GitHubRepositoryIdentityBinding.Record(
            Guid.NewGuid(), rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, target.Id, rows.Operation.CommitSha,
            rows.Operation.BranchName, NodeId, "Octo-Cat", "Hello.World", LocalCommitRowsSeed.Now, LocalCommitRowsSeed.Now.AddSeconds(1),
            LocalCommitRowsSeed.Now.AddSeconds(2), Guid.NewGuid());
        db.GitHubObservationTargets.Add(target);
        db.Events.Add(RunEvent.Record(
            target.EventId, rows.Run.Id, null, RunEventType.GitHubObservationTargetRecorded, ParticipantIdentity.ForHuman(),
            GitHubObservationTargetEventPayload.Build(target), target.RecordedAtUtc));
        db.GitHubRepositoryIdentityBindings.Add(binding);
        db.Events.Add(RunEvent.Record(
            binding.EventId, rows.Run.Id, null, RunEventType.GitHubRepositoryIdentityBound, ParticipantIdentity.ForHuman(),
            GitHubRepositoryIdentityBindingEventPayload.Build(binding), binding.RecordedAtUtc));
        await db.SaveChangesAsync();

        var source = new GitHubObservationSource(
            rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, rows.Operation.CommitSha, rows.Operation.BranchName);
        var targetView = new GitHubObservationTargetView(
            target.Id, target.EventId, GitHubObservationTarget.Host, target.Owner, target.Repository, target.RecordedAtUtc);
        var bindingView = new GitHubRepositoryIdentityBindingView(
            binding.Id, binding.EventId, binding.RepositoryNodeId, binding.Owner, binding.Repository, binding.ObservedAtUtc,
            binding.RecordedAtUtc);
        return new Seeded(
            rows,
            target,
            binding,
            new GitHubBranchPublicationScopeReading(
                source,
                new GitHubObservationTargetReading(GitHubObservationTargetReadingKind.Coherent, targetView),
                new GitHubRepositoryIdentityBindingReading(GitHubRepositoryIdentityBindingReadingKind.Coherent, bindingView)));
    }

    private static GitHubBranchPublicationApproval ApprovalFor(Seeded seeded, DateTimeOffset? approvedAt = null, Guid? runId = null) =>
        GitHubBranchPublicationApproval.Record(
            Guid.NewGuid(), Guid.NewGuid(), runId ?? seeded.Rows.Run.Id, seeded.Rows.Project.Id, seeded.Rows.Operation.Id,
            seeded.Rows.Workspace.Id, seeded.Rows.Operation.CommitSha, seeded.Rows.Operation.BranchName, seeded.Target.Id,
            seeded.Target.EventId, seeded.Target.Owner, seeded.Target.Repository, seeded.Target.RecordedAtUtc, seeded.Binding.Id,
            seeded.Binding.EventId, seeded.Binding.RepositoryNodeId, seeded.Binding.Owner, seeded.Binding.Repository,
            seeded.Binding.ObservedAtUtc, seeded.Binding.RecordedAtUtc, approvedAt ?? seeded.Binding.RecordedAtUtc.AddSeconds(1));

    private static RunEvent EventFor(Guid runId, GitHubBranchPublicationApproval approval) =>
        RunEvent.Record(
            approval.EventId, runId, null, RunEventType.GitHubBranchPublicationApproved, ParticipantIdentity.ForHuman(),
            GitHubBranchPublicationApprovalEventPayload.Build(approval), approval.ApprovedAtUtc);

    private async Task<GitHubBranchPublicationApproval> ApproveAsync(Seeded seeded, bool row = true, bool recordEvent = true)
    {
        var approval = ApprovalFor(seeded);
        await using var db = _fixture.CreateContext();
        if (row)
        {
            db.GitHubBranchPublicationApprovals.Add(approval);
        }

        if (recordEvent)
        {
            db.Events.Add(EventFor(seeded.Rows.Run.Id, approval));
        }

        await db.SaveChangesAsync();
        return approval;
    }

    private async Task<GitHubBranchPublicationApprovalReading> ReadAsync(Seeded seeded, GitHubBranchPublicationScopeReading? scope = null)
    {
        await using var db = _fixture.CreateContext();
        return await GitHubBranchPublicationApprovalReader.ReadAsync(
            db, seeded.Rows.Run.Id, scope ?? seeded.Scope, CancellationToken.None);
    }

    private async Task ExecuteAsync(params string[] statements)
    {
        await using var connection = new SqliteConnection(_fixture.ConnectionString + ";Pooling=False");
        await connection.OpenAsync();
        foreach (var statement in new[]
                 {
                     "PRAGMA foreign_keys = OFF",
                     "DROP TRIGGER IF EXISTS trg_github_branch_publication_approvals_no_update",
                     "DROP TRIGGER IF EXISTS trg_github_branch_publication_approvals_no_delete",
                 }.Concat(statements))
        {
            await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and identifiers this test generated.
            command.CommandText = statement;
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string Upper(Guid id) => id.ToString().ToUpperInvariant();

    private static string OwnEvent(Guid runId) =>
        $"\"EventType\" = '{RunEventType.GitHubBranchPublicationApproved}' AND \"RunId\" = '{Upper(runId)}'";

    [Fact]
    public async Task Nothing_recorded_is_none()
    {
        var seeded = await SeedAsync();

        var reading = await ReadAsync(seeded);

        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.None, reading.Kind);
        Assert.Null(reading.Approval);
    }

    [Fact]
    public async Task A_coherent_approval_and_its_one_human_event_read_back_exactly()
    {
        var seeded = await SeedAsync();
        var approval = await ApproveAsync(seeded);

        var reading = await ReadAsync(seeded);

        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Coherent, reading.Kind);
        var view = Assert.IsType<GitHubBranchPublicationApprovalView>(reading.Approval);
        Assert.Equal(approval.Id, view.Id);
        Assert.Equal(approval.EventId, view.EventId);
        Assert.Equal("github.com", view.Host);
        Assert.Equal("PushExactCommit", view.Action);
        Assert.Equal(1, view.PolicyVersion);
        Assert.Equal(seeded.Rows.Operation.Id, view.LocalCommitOperationId);
        Assert.Equal(seeded.Rows.Workspace.Id, view.WorkspaceId);
        Assert.Equal(seeded.Target.Id, view.TargetId);
        Assert.Equal(seeded.Binding.Id, view.BindingId);
        Assert.Equal(NodeId, view.RepositoryNodeId);
        Assert.Equal("octo-cat", view.Owner);
        Assert.Equal("hello.world", view.Repository);
        Assert.Equal(seeded.Rows.Operation.BranchName, view.BranchName);
        Assert.Equal(seeded.Rows.Operation.CommitSha, view.CommitSha);
        Assert.Equal(approval.ApprovedAtUtc, view.ApprovedAtUtc);
    }

    [Fact]
    public async Task An_approval_time_equal_to_the_binding_recording_time_is_coherent()
    {
        var seeded = await SeedAsync();
        var approval = ApprovalFor(seeded, approvedAt: seeded.Binding.RecordedAtUtc);
        await using (var db = _fixture.CreateContext())
        {
            db.GitHubBranchPublicationApprovals.Add(approval);
            db.Events.Add(EventFor(seeded.Rows.Run.Id, approval));
            await db.SaveChangesAsync();
        }

        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Coherent, (await ReadAsync(seeded)).Kind);
    }

    [Fact]
    public async Task A_row_without_its_event_and_an_event_without_its_row_are_each_incoherent_never_absent()
    {
        var withoutEvent = await SeedAsync();
        await ApproveAsync(withoutEvent, recordEvent: false);
        var withoutRow = await SeedAsync();
        await ApproveAsync(withoutRow, row: false);

        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Incoherent, (await ReadAsync(withoutEvent)).Kind);
        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Incoherent, (await ReadAsync(withoutRow)).Kind);
    }

    [Fact]
    public async Task A_record_or_event_with_no_whole_scope_is_an_orphan_not_absent_and_never_a_success()
    {
        var seeded = await SeedAsync();
        await ApproveAsync(seeded);
        var noBinding = seeded.Scope with { Binding = GitHubRepositoryIdentityBindingReading.None };
        var noTarget = seeded.Scope with { Target = GitHubObservationTargetReading.None };
        var noSource = seeded.Scope with { Source = null };

        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Incoherent, (await ReadAsync(seeded, noBinding)).Kind);
        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Incoherent, (await ReadAsync(seeded, noTarget)).Kind);
        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Incoherent, (await ReadAsync(seeded, noSource)).Kind);
    }

    [Fact]
    public async Task Without_a_whole_scope_and_without_any_record_the_reading_is_none()
    {
        var seeded = await SeedAsync();

        Assert.Equal(
            GitHubBranchPublicationApprovalReadingKind.None,
            (await ReadAsync(seeded, seeded.Scope with { Binding = GitHubRepositoryIdentityBindingReading.None })).Kind);
        Assert.Equal(
            GitHubBranchPublicationApprovalReadingKind.None,
            (await ReadAsync(seeded, seeded.Scope with { Source = null, Target = GitHubObservationTargetReading.None })).Kind);
    }

    [Fact]
    public async Task Every_broken_pinned_value_cross_link_time_actor_payload_or_count_is_incoherent_and_the_restored_record_is_coherent_again()
    {
        var seeded = await SeedAsync();
        var approval = await ApproveAsync(seeded);
        var runId = seeded.Rows.Run.Id;
        var id = Upper(approval.Id);
        var own = OwnEvent(runId);
        var someone = Upper(Guid.NewGuid());
        var someoneElsesEvent = $"\"Id\" = '{Upper(approval.EventId)}'";
        string Row(string assignments) => $"UPDATE {Approvals} SET {assignments} WHERE \"Id\" = '{id}'";
        string Event(string assignments) => $"UPDATE {Events} SET {assignments} WHERE {own}";

        (string Name, string[] Statements)[] cases =
        [
            ("project", [Row($"\"ProjectId\" = '{someone}'")]),
            ("operation", [Row($"\"LocalCommitOperationId\" = '{someone}'")]),
            ("workspace", [Row($"\"GitWorkspaceId\" = '{someone}'")]),
            ("commit", [Row($"\"CommitSha\" = '{new string('9', 40)}'")]),
            ("branch", [Row("\"BranchName\" = 'elsewhere'")]),
            ("target id", [Row($"\"TargetId\" = '{someone}'")]),
            ("target event", [Row($"\"TargetEventId\" = '{someone}'")]),
            ("target owner", [Row("\"TargetOwner\" = 'Octo-Cat'")]),
            ("target repository", [Row("\"TargetRepository\" = 'Hello.World'")]),
            ("target time", [Row("\"TargetRecordedAtUtc\" = '2001-01-01 00:00:00+00:00'")]),
            ("binding id", [Row($"\"BindingId\" = '{someone}'")]),
            ("binding event", [Row($"\"BindingEventId\" = '{someone}'")]),
            ("node", [Row("\"RepositoryNodeId\" = 'R_other'")]),
            ("binding owner", [Row("\"BindingOwner\" = 'octo-cat'")]),
            ("binding repository", [Row("\"BindingRepository\" = 'hello.world'")]),
            ("binding observed time", [Row("\"BindingObservedAtUtc\" = '2999-01-01 00:00:00+00:00'")]),
            ("binding recorded time", [Row("\"BindingRecordedAtUtc\" = '2999-01-01 00:00:00+00:00'")]),
            ("approved before the binding was recorded",
            [
                Row("\"ApprovedAtUtc\" = '2001-01-01 00:00:00+00:00'"),
                Event("\"OccurredAtUtc\" = '2001-01-01 00:00:00+00:00'"),
            ]),
            ("approval time differs from the event time", [Row("\"ApprovedAtUtc\" = '2999-01-01 00:00:00+00:00'")]),
            ("event time differs from the approval time", [Event("\"OccurredAtUtc\" = '2999-01-01 00:00:00+00:00'")]),
            ("approval names another event", [Row($"\"EventId\" = '{someone}'")]),
            ("approval belongs to another run", [Row($"\"RunId\" = '{someone}'")]),
            ("event is missing", [$"DELETE FROM {Events} WHERE {own}"]),
            ("event has another identifier", [Event($"\"Id\" = '{someone}'")]),
            ("event belongs to another run", [$"UPDATE {Events} SET \"RunId\" = '{someone}' WHERE {someoneElsesEvent}"]),
            ("payload names another node", [Event($"\"PayloadJson\" = replace(\"PayloadJson\", '{NodeId}', 'R_other')")]),
            ("payload is version two", [Event("\"PayloadJson\" = replace(\"PayloadJson\", '\"version\":1', '\"version\":2')")]),
            ("payload has an extra member", [Event("\"PayloadJson\" = replace(\"PayloadJson\", '}', ',\"x\":1}')")]),
            ("payload is over the bound", [Event($"\"PayloadJson\" = \"PayloadJson\" || '{new string(' ', 4200)}'")]),
            ("payload is empty", [Event("\"PayloadJson\" = ''")]),
            ("payload hides a large suffix behind an embedded NUL",
            [Event("\"PayloadJson\" = \"PayloadJson\" || char(0) || replace(hex(zeroblob(50000)), '0', 'x')")]),
            ("event was authored by an agent",
            [Event("\"ActorKind\" = 'Agent', \"ActorAgentRole\" = 'Planner', \"ActorAgentProvider\" = 'Codex'")]),
            ("event actor kind is an unknown stored value", [Event("\"ActorKind\" = 'UnrecognizedFutureValue'")]),
            ("event actor role is an unknown stored value", [Event("\"ActorAgentRole\" = 'UnrecognizedFutureValue'")]),
            ("event actor provider is an unknown stored value", [Event("\"ActorAgentProvider\" = 'UnrecognizedFutureValue'")]),
            ("event names an attempt", [Event($"\"AttemptId\" = '{Upper(seeded.Rows.Operation.CodeReviewAttemptId)}'")]),
            ("run owns a second event of the type",
            [
                $"INSERT INTO {Events} (\"Id\", \"RunId\", \"AttemptId\", \"EventType\", \"ActorKind\", \"ActorAgentRole\", "
                + "\"ActorAgentProvider\", \"PayloadJson\", \"OccurredAtUtc\") "
                + $"SELECT '{someone}', \"RunId\", \"AttemptId\", \"EventType\", \"ActorKind\", \"ActorAgentRole\", "
                + $"\"ActorAgentProvider\", \"PayloadJson\", \"OccurredAtUtc\" FROM {Events} WHERE {own}",
            ]),
            ("commit is oversized", [Row("\"CommitSha\" = replace(hex(zeroblob(50)), '0', 'c')")]),
            ("branch is oversized", [Row("\"BranchName\" = replace(hex(zeroblob(1600)), '0', 'b')")]),
            ("node is oversized", [Row("\"RepositoryNodeId\" = replace(hex(zeroblob(300)), '0', 'n')")]),
            ("node hides a large suffix behind an embedded NUL",
            [Row("\"RepositoryNodeId\" = 'ab' || char(0) || replace(hex(zeroblob(50000)), '0', 'x')")]),
            ("target owner is oversized", [Row("\"TargetOwner\" = replace(hex(zeroblob(150)), '0', 'o')")]),
            ("binding repository is oversized", [Row("\"BindingRepository\" = replace(hex(zeroblob(300)), '0', 'r')")]),
        ];

        foreach (var (name, statements) in cases)
        {
            await using (var scratch = new SqliteConnection(_fixture.ConnectionString + ";Pooling=False"))
            {
                await scratch.OpenAsync();
                await using var backup = scratch.CreateCommand();
                backup.CommandText = $"CREATE TABLE bk_approvals AS SELECT * FROM {Approvals}; CREATE TABLE bk_events AS SELECT * FROM {Events};";
                await backup.ExecuteNonQueryAsync();
            }

            await ExecuteAsync(statements);
            var broken = await ReadAsync(seeded);
            Assert.True(broken.Kind == GitHubBranchPublicationApprovalReadingKind.Incoherent, $"{name}: {broken.Kind}");
            Assert.Null(broken.Approval);

            await ExecuteAsync(
                $"DELETE FROM {Approvals}",
                $"INSERT INTO {Approvals} SELECT * FROM bk_approvals",
                $"DELETE FROM {Events}",
                $"INSERT INTO {Events} SELECT * FROM bk_events",
                "DROP TABLE bk_approvals",
                "DROP TABLE bk_events");
            var restored = await ReadAsync(seeded);
            Assert.True(restored.Kind == GitHubBranchPublicationApprovalReadingKind.Coherent, $"{name}: not restored ({restored.Kind})");
        }
    }

    [Fact]
    public async Task A_record_of_another_run_that_names_this_runs_operation_target_or_binding_is_incoherent_never_absent()
    {
        var seeded = await SeedAsync();
        var other = await SeedAsync();
        var foreign = ApprovalFor(other);
        await using (var db = _fixture.CreateContext())
        {
            db.GitHubBranchPublicationApprovals.Add(foreign);
            db.Events.Add(EventFor(other.Rows.Run.Id, foreign));
            await db.SaveChangesAsync();
        }

        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.None, (await ReadAsync(seeded)).Kind);
        foreach (var column in new[] { "LocalCommitOperationId", "TargetId", "BindingId" })
        {
            var value = column switch
            {
                "LocalCommitOperationId" => seeded.Rows.Operation.Id,
                "TargetId" => seeded.Target.Id,
                _ => seeded.Binding.Id,
            };
            await ExecuteAsync($"UPDATE {Approvals} SET \"{column}\" = '{Upper(value)}' WHERE \"Id\" = '{Upper(foreign.Id)}'");

            Assert.True(
                (await ReadAsync(seeded)).Kind == GitHubBranchPublicationApprovalReadingKind.Incoherent,
                $"A foreign record naming this run's {column} is not absent.");

            await ExecuteAsync($"DELETE FROM {Approvals} WHERE \"Id\" = '{Upper(foreign.Id)}'");
            await using var db = _fixture.CreateContext();
            db.GitHubBranchPublicationApprovals.Add(foreign);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Reading_writes_nothing()
    {
        var seeded = await SeedAsync();
        await ApproveAsync(seeded);
        async Task<string> Counts()
        {
            await using var db = _fixture.CreateContext();
            return $"{await db.Events.CountAsync()}|{await db.GitHubBranchPublicationApprovals.CountAsync()}|{await db.Runs.CountAsync()}";
        }

        var before = await Counts();
        await ReadAsync(seeded);

        Assert.Equal(before, await Counts());
    }
}
