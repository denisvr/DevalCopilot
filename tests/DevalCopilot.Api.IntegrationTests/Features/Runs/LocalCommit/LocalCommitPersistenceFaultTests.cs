using System.Net;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0029 persistence faults at every durable boundary of one local commit, injected as REAL SQLite faults (a trigger that
/// aborts exactly one write) against the production host, real Git and a real file-backed database. Each case proves the host
/// fails closed in-process, never invents an outcome and never retries Git, and that the next real host start decides from the
/// exact recorded evidence and records its event exactly once.
/// </summary>
public sealed class LocalCommitPersistenceFaultTests : LocalCommitTestBase
{
    private const string Completed = "local_commit.completed";

    private sealed class ThrowingNotifier : IRunEventNotifier
    {
        public int Calls;

        public Task NotifyRunAdvancedAsync(Guid runId, long latestSequence, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("The notification transport failed after the facts were durable.");
        }
    }

    [Fact]
    public async Task A_notifier_that_fails_after_every_durable_fact_changes_no_recorded_outcome()
    {
        var notifier = new ThrowingNotifier();
        var (host, _) = StartScripted(
            supervisor: true,
            configure: services =>
            {
                services.RemoveAll<IRunEventNotifier>();
                services.AddSingleton<IRunEventNotifier>(notifier);
            });
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);

        var response = await PostAsync(host, ids.RunId, ids);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await WaitForAsync(host, ids.RunId, status => status.Operation?.Status == "Completed");

        Assert.True(notifier.Calls >= 3, "admission, execution and the outcome each tried to notify");
        Assert.Equal(RunLifecycle.Completed, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(
            ["local_commit.admitted", "local_commit.executing", Completed],
            (await EventTypeRowsAsync(ids.RunId)).Where(type => type.StartsWith("local_commit.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_failed_acquisition_receipt_never_reaches_the_reference_and_restart_keeps_the_lock_under_attention()
    {
        var (host1, script1) = StartScripted(supervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host1, Scene);
        await ExecuteSqlAsync(
            "CREATE TRIGGER fault_receipt BEFORE UPDATE ON local_commit_operations WHEN NEW.\"IndexAcquiredAtUtc\" IS NOT NULL "
            + "BEGIN SELECT RAISE(ABORT, 'injected_receipt_fault'); END;");
        var mainBefore = Scene.MainRepositoryFingerprint();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host1, ids.RunId, ids)).StatusCode);

        await WaitUntilAsync(() => Task.FromResult(script1.LastAcquisition is not null));
        await Task.Delay(1500);

        Assert.DoesNotContain(nameof(ILocalCommitRepository.PromoteRefAsync), script1.Calls);
        Assert.Equal(Scene.BaselineCommit, BranchTip());
        await AssertReservedAsync(ids, WorkspaceStatus.Committing, LocalCommitStatus.Executing);
        Assert.Equal(["local_commit.admitted", "local_commit.executing"], (await EventTypeRowsAsync(ids.RunId)).Where(IsLocal));
        host1.Dispose();
        await ExecuteSqlAsync("DROP TRIGGER fault_receipt");

        // The previous host's lock capability survives in this process exactly as a lost process's lock file would: restart
        // never adopts or removes it, and neither run nor workspace is released.
        using var host2 = StartHost(runSupervisor: false);
        var attention = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.NeedsAttention, attention.Status);
        Assert.Equal("local_commit.unpromoted_state_unknown", attention.OutcomeReasonCode);
        Assert.Equal(WorkspaceStatus.NeedsAttention, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.True(File.Exists(Scene.IndexPath + ".lock"));
        Assert.Equal(1, await EventCountAsync(ids.RunId, "local_commit.needs_attention"));

        using var host3 = StartHost(runSupervisor: false);
        Assert.Equal(1, await EventCountAsync(ids.RunId, "local_commit.needs_attention"));

        // Only once the lock is genuinely gone is the unpromoted state exact: the operation is interrupted and the run with it.
        Assert.True(await script1.ReleaseRetainedAsync());
        host2.Dispose();
        host3.Dispose();
        using var host4 = StartHost(runSupervisor: false);
        var interrupted = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.Interrupted, interrupted.Status);
        Assert.Equal("local_commit.interrupted_unpromoted", interrupted.OutcomeReasonCode);
        Assert.Equal(RunLifecycle.Interrupted, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(1, await EventCountAsync(ids.RunId, "local_commit.interrupted"));
        Assert.Equal(Scene.BaselineCommit, BranchTip());
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
    }

    [Fact]
    public async Task A_failed_replacement_plan_after_the_reference_moved_is_never_completed_by_inference()
    {
        var (host1, script1) = StartScripted(supervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host1, Scene);
        await ExecuteSqlAsync(
            "CREATE TRIGGER fault_plan BEFORE UPDATE ON local_commit_operations WHEN NEW.\"IndexQuarantineName\" IS NOT NULL "
            + "BEGIN SELECT RAISE(ABORT, 'injected_plan_fault'); END;");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host1, ids.RunId, ids)).StatusCode);

        var operation = await OperationRowAsync(ids.RunId);
        await WaitUntilAsync(() => Task.FromResult(BranchTip() == operation.CommitSha));
        await Task.Delay(1500);

        Assert.DoesNotContain(nameof(ILocalCommitRepository.PromoteHeldIndexAsync), script1.Calls);
        await AssertReservedAsync(ids, WorkspaceStatus.Committing, LocalCommitStatus.Executing);
        host1.Dispose();
        await ExecuteSqlAsync("DROP TRIGGER fault_plan");

        using var host2 = StartHost(runSupervisor: false);
        var attention = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.NeedsAttention, attention.Status);
        Assert.Equal("local_commit.unknown_index_lock", attention.OutcomeReasonCode);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);

        // Even with the lock gone, no durable quarantine plan exists, so the index is never finished or guessed: it stays attention.
        Assert.True(await script1.ReleaseRetainedAsync());
        host2.Dispose();
        using var host3 = StartHost(runSupervisor: false);
        var still = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.NeedsAttention, still.Status);
        Assert.Equal("local_commit.index_state_unrecognized", still.OutcomeReasonCode);
        Assert.Equal(WorkspaceStatus.NeedsAttention, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(operation.CommitSha, BranchTip());
        Assert.DoesNotContain(nameof(ILocalCommitRepository.AcquirePendingIndexEffectsAsync), script1.Calls);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("event")]
    public async Task A_failed_terminal_record_leaves_the_exact_delivery_for_restart_to_complete_exactly_once(string fault)
    {
        var (host1, script1) = StartScripted(supervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host1, Scene);
        await ExecuteSqlAsync(fault == "status"
            ? "CREATE TRIGGER fault_terminal BEFORE UPDATE ON local_commit_operations WHEN NEW.\"Status\" = 'Completed' "
                + "BEGIN SELECT RAISE(ABORT, 'injected_terminal_fault'); END;"
            : "CREATE TRIGGER fault_terminal BEFORE INSERT ON events WHEN NEW.\"EventType\" = 'local_commit.completed' "
                + "BEGIN SELECT RAISE(ABORT, 'injected_terminal_fault'); END;");
        var mainBefore = Scene.MainRepositoryFingerprint();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host1, ids.RunId, ids)).StatusCode);

        var prepared = await OperationRowAsync(ids.RunId);
        await WaitUntilAsync(() => Task.FromResult(
            BranchTip() == prepared.CommitSha && !File.Exists(Scene.IndexPath + ".lock")
            && script1.Calls.Contains(nameof(ILocalCommitRepository.PromoteHeldIndexAsync))));
        await Task.Delay(1500);

        // The real delivery completed in Git; only its durable outcome was refused, atomically with its event.
        await AssertReservedAsync(ids, WorkspaceStatus.Committing, LocalCommitStatus.Executing);
        Assert.Equal(0, await EventCountAsync(ids.RunId, Completed));
        Assert.Contains(prepared.CommitSha, Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName));
        host1.Dispose();
        await ExecuteSqlAsync("DROP TRIGGER fault_terminal");

        using var host2 = StartHost(runSupervisor: false);
        var completed = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.Completed, completed.Status);
        Assert.Equal(prepared.CommitSha, completed.CommitSha);
        Assert.Equal(RunLifecycle.Completed, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(1, await EventCountAsync(ids.RunId, Completed));
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        Assert.False(Directory.Exists(Scene.ArtifactLeaf(prepared.PreparedIndexRelativePath)));

        var eventsAfterRecovery = (await EventTypeRowsAsync(ids.RunId)).Length;
        host2.Dispose();
        using var host3 = StartHost(runSupervisor: false);
        Assert.Equal(eventsAfterRecovery, (await EventTypeRowsAsync(ids.RunId)).Length);
        Assert.Equal(LocalCommitStatus.Completed, (await OperationRowAsync(ids.RunId)).Status);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
    }

    private static bool IsLocal(string type) => type.StartsWith("local_commit.", StringComparison.Ordinal);
}
