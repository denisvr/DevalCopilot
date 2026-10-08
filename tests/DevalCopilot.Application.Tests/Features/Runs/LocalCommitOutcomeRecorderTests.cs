using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Policies.LocalCommit;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The single recorded outcome of one local-commit operation and its run, workspace and event consequences, in a real SQLite
/// database. Every case proves the facts are written together exactly once, that a populated change tracker is never trusted, that
/// two recorders can never both decide the same operation, and that a notifier failure never changes a durable fact.
/// </summary>
public sealed class LocalCommitOutcomeRecorderTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class RecordingNotifier : IRunEventNotifier
    {
        public List<(Guid RunId, long Sequence)> Calls { get; } = [];

        public bool Throw { get; set; }

        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken)
        {
            Calls.Add((runId, latestSequence));
            return Throw ? throw new InvalidOperationException("transport down") : Task.CompletedTask;
        }
    }

    private LocalCommitOutcomeRecorder Recorder(DevalCopilotDbContext db, RecordingNotifier? notifier = null) =>
        new(db, new FixedTimeProvider(LocalCommitRowsSeed.Now.AddMinutes(5)), notifier);

    private async Task<string[]> LocalEventsAsync(Guid runId)
    {
        await using var db = _fixture.CreateContext();
        return await db.Events.AsNoTracking().Where(runEvent => runEvent.RunId == runId).OrderBy(runEvent => runEvent.Sequence)
            .Select(runEvent => runEvent.EventType).ToArrayAsync();
    }

    private async Task<LocalCommitRows> SeedExecutingAsync(WorkspaceStatus workspace = WorkspaceStatus.Committing)
    {
        await using var db = _fixture.CreateContext();
        return await LocalCommitRowsSeed.SeedAsync(db, LocalCommitStatus.Executing, workspace);
    }

    private async Task<(LocalCommitOperation Operation, Run Run, GitWorkspace Workspace)> ReadAsync(LocalCommitRows rows)
    {
        await using var db = _fixture.CreateContext();
        return (
            await db.LocalCommitOperations.AsNoTracking().SingleAsync(candidate => candidate.Id == rows.Operation.Id),
            await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == rows.Run.Id),
            await db.GitWorkspaces.AsNoTracking().SingleAsync(candidate => candidate.Id == rows.Workspace.Id));
    }

    [Fact]
    public async Task Completion_completes_the_operation_and_run_releases_a_consistent_workspace_and_records_one_event()
    {
        var rows = await SeedExecutingAsync();
        var notifier = new RecordingNotifier();
        await using (var db = _fixture.CreateContext())
        {
            var view = await Recorder(db, notifier).CompleteAsync(rows.Operation.Id, sourceConsistent: true, CancellationToken.None);
            Assert.Equal(nameof(LocalCommitStatus.Completed), view!.Status);
            Assert.Equal(rows.Operation.CommitSha, view.CommitSha);
        }

        var (operation, run, workspace) = await ReadAsync(rows);
        Assert.Equal(LocalCommitStatus.Completed, operation.Status);
        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
        Assert.Equal(["local_commit.completed"], (await LocalEventsAsync(rows.Run.Id)).Where(type => type.StartsWith("local_commit.", StringComparison.Ordinal)));
        var single = Assert.Single(notifier.Calls);
        Assert.Equal(rows.Run.Id, single.RunId);
        await using var check = _fixture.CreateContext();
        var recorded = await check.Events.AsNoTracking().SingleAsync(runEvent => runEvent.RunId == rows.Run.Id && runEvent.EventType == "local_commit.completed");
        Assert.Equal(single.Sequence, recorded.Sequence);
        using var payload = JsonDocument.Parse(recorded.PayloadJson);
        Assert.Equal(rows.Operation.CommitSha, payload.RootElement.GetProperty("commit").GetString());
    }

    [Fact]
    public async Task Completion_with_changed_working_files_still_completes_the_commit_but_never_labels_the_workspace_clean()
    {
        var rows = await SeedExecutingAsync();
        await using (var db = _fixture.CreateContext())
        {
            await Recorder(db).CompleteAsync(rows.Operation.Id, sourceConsistent: false, CancellationToken.None);
        }

        var (operation, run, workspace) = await ReadAsync(rows);
        Assert.Equal(LocalCommitStatus.Completed, operation.Status);
        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
        Assert.Equal("local_commit.source_changed_after_commit", workspace.LastFailureReasonCode);
    }

    [Fact]
    public async Task A_definitely_unpromoted_failure_fails_the_run_and_releases_the_reservation()
    {
        var rows = await SeedExecutingAsync();
        await using (var db = _fixture.CreateContext())
        {
            await Recorder(db).FailAsync(rows.Operation.Id, "local_commit.index_acquisition_unproven", CancellationToken.None);
        }

        var (operation, run, workspace) = await ReadAsync(rows);
        Assert.Equal(LocalCommitStatus.Failed, operation.Status);
        Assert.Equal("local_commit.index_acquisition_unproven", operation.OutcomeReasonCode);
        Assert.Equal(RunLifecycle.Failed, run.Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
        Assert.Equal(1, (await LocalEventsAsync(rows.Run.Id)).Count(type => type == "local_commit.failed"));
    }

    [Fact]
    public async Task A_startup_interruption_interrupts_the_run_and_releases_the_reservation()
    {
        var rows = await SeedExecutingAsync();
        await using (var db = _fixture.CreateContext())
        {
            await Recorder(db).InterruptAsync(rows.Operation.Id, "local_commit.interrupted_unpromoted", CancellationToken.None);
        }

        var (operation, run, workspace) = await ReadAsync(rows);
        Assert.Equal(LocalCommitStatus.Interrupted, operation.Status);
        Assert.Equal(RunLifecycle.Interrupted, run.Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
        Assert.Equal(1, (await LocalEventsAsync(rows.Run.Id)).Count(type => type == "local_commit.interrupted"));
    }

    [Fact]
    public async Task Ambiguity_changes_neither_the_run_nor_the_reservation_and_invents_no_decision()
    {
        var rows = await SeedExecutingAsync();
        await using (var db = _fixture.CreateContext())
        {
            await Recorder(db).NeedsAttentionAsync(rows.Operation.Id, "local_commit.release_unproven", CancellationToken.None);
        }

        var (operation, run, workspace) = await ReadAsync(rows);
        Assert.Equal(LocalCommitStatus.NeedsAttention, operation.Status);
        Assert.False(operation.IsTerminal);
        Assert.Equal(RunLifecycle.Running, run.Lifecycle);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
        Assert.True(workspace.IsAttentionFromLocalCommit);
        Assert.Equal(1, (await LocalEventsAsync(rows.Run.Id)).Count(type => type == "local_commit.needs_attention"));
    }

    [Fact]
    public async Task The_attention_a_local_commit_set_is_released_by_a_later_exact_completion_but_a_foreign_one_never_is()
    {
        var attention = await SeedExecutingAsync();
        await using (var db = _fixture.CreateContext())
        {
            var recorder = Recorder(db);
            await recorder.NeedsAttentionAsync(attention.Operation.Id, "local_commit.unknown_index_lock", CancellationToken.None);
            await recorder.CompleteAsync(attention.Operation.Id, sourceConsistent: true, CancellationToken.None);
        }

        var (operation, run, workspace) = await ReadAsync(attention);
        Assert.Equal(LocalCommitStatus.Completed, operation.Status);
        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);

        var foreign = await SeedExecutingAsync(WorkspaceStatus.NeedsAttention);
        await using (var db = _fixture.CreateContext())
        {
            await db.GitWorkspaces.Where(candidate => candidate.Id == foreign.Workspace.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.LastFailureReasonCode, "workspaces.reconciliation_head_diverged"));
            await Recorder(db).FailAsync(foreign.Operation.Id, "local_commit.failed", CancellationToken.None);
        }

        var (failed, _, stillAttention) = await ReadAsync(foreign);
        Assert.Equal(LocalCommitStatus.Failed, failed.Status);
        Assert.Equal(WorkspaceStatus.NeedsAttention, stillAttention.Status);
        Assert.Equal("workspaces.reconciliation_head_diverged", stillAttention.LastFailureReasonCode);
    }

    [Fact]
    public async Task A_recorded_terminal_operation_is_returned_unchanged_and_writes_no_second_event()
    {
        var rows = await SeedExecutingAsync();
        await using var db = _fixture.CreateContext();
        var recorder = Recorder(db);
        await recorder.CompleteAsync(rows.Operation.Id, sourceConsistent: true, CancellationToken.None);
        var before = (await LocalEventsAsync(rows.Run.Id)).Length;

        var again = await recorder.FailAsync(rows.Operation.Id, "local_commit.failed", CancellationToken.None);
        var interrupted = await recorder.InterruptAsync(rows.Operation.Id, "local_commit.interrupted", CancellationToken.None);
        var attention = await recorder.NeedsAttentionAsync(rows.Operation.Id, "local_commit.ambiguous", CancellationToken.None);

        Assert.Equal(nameof(LocalCommitStatus.Completed), again!.Status);
        Assert.Equal(nameof(LocalCommitStatus.Completed), interrupted!.Status);
        Assert.Equal(nameof(LocalCommitStatus.Completed), attention!.Status);
        Assert.Equal(before, (await LocalEventsAsync(rows.Run.Id)).Length);
        Assert.Null(await recorder.CompleteAsync(Guid.NewGuid(), true, CancellationToken.None));
    }

    [Fact]
    public async Task A_populated_tracker_with_stale_copies_is_never_trusted_for_the_operation_run_or_workspace()
    {
        var rows = await SeedExecutingAsync();
        await using var stale = _fixture.CreateContext();
        // The tracker already holds Executing/Running/Committing copies, as a reused or long-lived context would.
        var trackedOperation = await stale.LocalCommitOperations.SingleAsync(candidate => candidate.Id == rows.Operation.Id);
        await stale.Runs.SingleAsync(candidate => candidate.Id == rows.Run.Id);
        await stale.GitWorkspaces.SingleAsync(candidate => candidate.Id == rows.Workspace.Id);
        Assert.Equal(LocalCommitStatus.Executing, trackedOperation.Status);

        await using (var other = _fixture.CreateContext())
        {
            await Recorder(other).CompleteAsync(rows.Operation.Id, sourceConsistent: true, CancellationToken.None);
        }

        var view = await Recorder(stale).FailAsync(rows.Operation.Id, "local_commit.failed", CancellationToken.None);

        Assert.Equal(nameof(LocalCommitStatus.Completed), view!.Status);
        var (operation, run, workspace) = await ReadAsync(rows);
        Assert.Equal(LocalCommitStatus.Completed, operation.Status);
        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
        var events = await LocalEventsAsync(rows.Run.Id);
        Assert.Equal(1, events.Count(type => type == "local_commit.completed"));
        Assert.DoesNotContain("local_commit.failed", events);
    }

    [Fact]
    public async Task Two_recorders_that_both_read_Executing_can_never_both_record_an_outcome()
    {
        var rows = await SeedExecutingAsync();
        await using var inner = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(inner);
        faulting.BeforeSaveChanges = async cancellationToken =>
        {
            // A competing recorder commits its decision between this recorder's reads and its single save.
            await using var competitor = _fixture.CreateContext();
            await Recorder(competitor).InterruptAsync(rows.Operation.Id, "local_commit.interrupted_unpromoted", cancellationToken);
        };

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => new LocalCommitOutcomeRecorder(faulting, new FixedTimeProvider(LocalCommitRowsSeed.Now), null)
                .CompleteAsync(rows.Operation.Id, sourceConsistent: true, CancellationToken.None));

        var (operation, run, workspace) = await ReadAsync(rows);
        Assert.Equal(LocalCommitStatus.Interrupted, operation.Status);
        Assert.Equal(RunLifecycle.Interrupted, run.Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
        var events = await LocalEventsAsync(rows.Run.Id);
        Assert.Equal(1, events.Count(type => type == "local_commit.interrupted"));
        Assert.DoesNotContain("local_commit.completed", events);
    }

    [Fact]
    public async Task A_notifier_failure_after_the_commit_never_changes_or_hides_the_recorded_outcome()
    {
        var rows = await SeedExecutingAsync();
        var notifier = new RecordingNotifier { Throw = true };
        await using (var db = _fixture.CreateContext())
        {
            var view = await Recorder(db, notifier).CompleteAsync(rows.Operation.Id, sourceConsistent: true, CancellationToken.None);
            Assert.Equal(nameof(LocalCommitStatus.Completed), view!.Status);
        }

        Assert.Single(notifier.Calls);
        var (operation, run, _) = await ReadAsync(rows);
        Assert.Equal(LocalCommitStatus.Completed, operation.Status);
        Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
        Assert.Equal(1, (await LocalEventsAsync(rows.Run.Id)).Count(type => type == "local_commit.completed"));
    }

    [Fact]
    public async Task A_cancelled_notification_still_propagates_after_the_durable_fact()
    {
        var rows = await SeedExecutingAsync();
        await using var db = _fixture.CreateContext();
        var notifier = new CancellingNotifier();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Recorder(db, notifier).NeedsAttentionAsync(rows.Operation.Id, "local_commit.release_unproven", CancellationToken.None));

        var (operation, _, _) = await ReadAsync(rows);
        Assert.Equal(LocalCommitStatus.NeedsAttention, operation.Status);
    }

    private sealed class CancellingNotifier : IRunEventNotifier
    {
        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken) =>
            throw new OperationCanceledException("host stopping");
    }

    private LocalCommitOutcomeRecorder Recorder(DevalCopilotDbContext db, IRunEventNotifier notifier) =>
        new(db, new FixedTimeProvider(LocalCommitRowsSeed.Now.AddMinutes(5)), notifier);
}
