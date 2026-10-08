using System.Net;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0029 restart reconciliation through the REAL host: a host loss is simulated at each external-effect boundary by an
/// <see cref="OperationCanceledException"/> after (or before) the real Git effect, the host is disposed, and the next real host
/// start runs the production startup recovery over the same file-backed SQLite database and real Git state. A surviving lock
/// capability is held in this process exactly as a dead process's lock file would remain on disk: it is never adopted.
/// </summary>
public sealed class LocalCommitRestartRecoveryTests : LocalCommitTestBase
{
    public enum Boundary
    {
        BeforeMarker,
        AfterMarkerBeforeAcquire,
        AfterAcquireBeforeRef,
        AfterRefBeforePlan,
        AfterPlanBeforeIndex,
        AfterIndexBeforeOutcome,
    }

    /// <summary>The scripted loss point of each boundary. Before-effect boundaries lose the host at the hook that precedes the real
    /// effect; after-effect boundaries only after the real effect has returned, and the report says what it returned.</summary>
    private static LossPoint? LossPointOf(Boundary boundary) => boundary switch
    {
        Boundary.BeforeMarker => null,
        Boundary.AfterMarkerBeforeAcquire => LossPoint.BeforeAcquire,
        Boundary.AfterAcquireBeforeRef => LossPoint.BeforePromoteRef,
        Boundary.AfterRefBeforePlan => LossPoint.AfterPromoteRef,
        Boundary.AfterPlanBeforeIndex => LossPoint.BeforePromoteHeldIndex,
        _ => LossPoint.AfterPromoteHeldIndex,
    };

    private static readonly TimeSpan LossBound = TimeSpan.FromSeconds(60);

    /// <summary>Seeds a lineage, admits the one operation and loses the host at <paramref name="boundary"/>. The host is stopped only
    /// after the scripted repository signalled that exact point: an after-effect boundary requires the REAL effect to have returned
    /// successfully (a refusal or failure is reported with its bounded outcome, never taken as a promotion), and no real effect may
    /// be in flight when the host is stopped or after it. Nothing sleeps and nothing polls.</summary>
    private async Task<(LocalCommitLineageIds Ids, ScriptedLocalCommitRepository Script, LocalCommitOperation Operation)> CrashAtAsync(
        Boundary boundary, bool disposeHost = true)
    {
        var (host, script) = StartScripted(supervisor: boundary != Boundary.BeforeMarker);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var point = LossPointOf(boundary);
        if (point is { } armed)
        {
            script.ArmLoss(armed);
        }

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);
        var operation = await OperationRowAsync(ids.RunId);
        if (point is not null)
        {
            LossReport report;
            try
            {
                report = await script.LossReached.WaitAsync(LossBound);
            }
            catch (TimeoutException)
            {
                throw new Xunit.Sdk.XunitException(
                    $"The {point} loss point was not reached within {LossBound.TotalSeconds:0} s; calls: {string.Join(", ", script.Calls)}.");
            }

            Assert.True(report.Satisfied, "The selected boundary was not reached: " + report.Detail);
            Assert.Equal(0, report.OutstandingEffects);
            Assert.Equal(0, script.OutstandingEffects);
        }

        if (disposeHost)
        {
            host.Dispose();
            Assert.Equal(0, script.OutstandingEffects);
            Assert.Empty(script.CallsAfterLoss);
        }

        return (ids, script, operation);
    }

    private async Task AssertFlightAsync(Boundary boundary, LocalCommitLineageIds ids, LocalCommitOperation prepared)
    {
        var tip = BranchTip();
        var promoted = boundary is Boundary.AfterRefBeforePlan or Boundary.AfterPlanBeforeIndex or Boundary.AfterIndexBeforeOutcome;
        Assert.Equal(promoted ? prepared.CommitSha : Scene.BaselineCommit, tip);
        var lockHeld = boundary is Boundary.AfterAcquireBeforeRef or Boundary.AfterRefBeforePlan or Boundary.AfterPlanBeforeIndex;
        Assert.Equal(lockHeld, File.Exists(Scene.IndexPath + ".lock"));
        var expectedStatus = boundary == Boundary.BeforeMarker ? LocalCommitStatus.Prepared : LocalCommitStatus.Executing;
        await AssertReservedAsync(ids, WorkspaceStatus.Committing, expectedStatus);
        var row = await OperationRowAsync(ids.RunId);
        Assert.Equal(boundary >= Boundary.AfterAcquireBeforeRef, row.IndexAcquiredAtUtc is not null);
        Assert.Equal(boundary >= Boundary.AfterPlanBeforeIndex, row.IndexReplacementPlannedAtUtc is not null);
    }

    [Theory]
    [InlineData(Boundary.BeforeMarker, LocalCommitStatus.Interrupted, "local_commit.interrupted_before_execution")]
    [InlineData(Boundary.AfterMarkerBeforeAcquire, LocalCommitStatus.Interrupted, "local_commit.interrupted_unpromoted")]
    [InlineData(Boundary.AfterAcquireBeforeRef, LocalCommitStatus.NeedsAttention, "local_commit.unpromoted_state_unknown")]
    [InlineData(Boundary.AfterRefBeforePlan, LocalCommitStatus.NeedsAttention, "local_commit.unknown_index_lock")]
    [InlineData(Boundary.AfterPlanBeforeIndex, LocalCommitStatus.NeedsAttention, "local_commit.unknown_index_lock")]
    [InlineData(Boundary.AfterIndexBeforeOutcome, LocalCommitStatus.Completed, null)]
    public async Task Restart_after_each_boundary_decides_only_from_exact_recorded_evidence(
        Boundary boundary, LocalCommitStatus expected, string? reason)
    {
        var (ids, script, prepared) = await CrashAtAsync(boundary);
        await AssertFlightAsync(boundary, ids, prepared);
        var mainBefore = Scene.MainRepositoryFingerprint();

        using var host = StartHost(runSupervisor: false);

        var operation = await OperationRowAsync(ids.RunId);
        Assert.Equal(expected, operation.Status);
        Assert.Equal(reason, operation.OutcomeReasonCode);
        var run = await RunRowAsync(ids.RunId);
        var workspace = await WorkspaceRowAsync(ids.WorkspaceId);
        switch (expected)
        {
            case LocalCommitStatus.Interrupted:
                Assert.Equal(RunLifecycle.Interrupted, run.Lifecycle);
                Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
                Assert.Equal(Scene.BaselineCommit, BranchTip());
                break;
            case LocalCommitStatus.Completed:
                Assert.Equal(RunLifecycle.Completed, run.Lifecycle);
                Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
                Assert.Equal(prepared.CommitSha, BranchTip());
                break;
            default:
                Assert.Equal(RunLifecycle.Running, run.Lifecycle);
                Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
                Assert.True(workspace.IsAttentionFromLocalCommit);
                Assert.True(File.Exists(Scene.IndexPath + ".lock"), "an unresolved lock is never removed by restart");
                break;
        }

        // Recovery never moved the main checkout, and it records exactly one terminal-or-attention event for the decision.
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        var decisionEvent = expected switch
        {
            LocalCommitStatus.Interrupted => "local_commit.interrupted",
            LocalCommitStatus.Completed => "local_commit.completed",
            _ => "local_commit.needs_attention",
        };
        Assert.Equal(1, await EventCountAsync(ids.RunId, decisionEvent));
        var eventTotal = (await EventTypeRowsAsync(ids.RunId)).Length;
        host.Dispose();
        using var again = StartHost(runSupervisor: false);
        Assert.Equal(eventTotal, (await EventTypeRowsAsync(ids.RunId)).Length);

        if (script.LastAcquisition is not null && File.Exists(Scene.IndexPath + ".lock"))
        {
            Assert.True(await script.ReleaseRetainedAsync());
        }
    }

    [Fact]
    public async Task A_lock_that_vanished_before_restart_lets_the_recorded_plan_be_finished_with_a_new_exclusive_acquisition()
    {
        var (ids, script, prepared) = await CrashAtAsync(Boundary.AfterPlanBeforeIndex);
        Assert.True(await script.ReleaseRetainedAsync());
        var preimageIndex = File.ReadAllBytes(Scene.IndexPath);
        Assert.False(File.Exists(Scene.IndexPath + ".lock"));

        using var host = StartHost(runSupervisor: false);

        var operation = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.Completed, operation.Status);
        Assert.Equal(RunLifecycle.Completed, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(prepared.CommitSha, BranchTip());
        Assert.NotEqual(preimageIndex, File.ReadAllBytes(Scene.IndexPath));
        Assert.Equal(string.Empty, Scene.RunWorkspaceGit("diff-index", "--cached", "HEAD").Trim());
        Assert.False(File.Exists(Scene.IndexPath + ".lock"));
        Assert.Equal(1, await EventCountAsync(ids.RunId, "local_commit.completed"));
        Assert.NotNull(operation.IndexAcquiredAtUtc);
        Assert.False(Directory.Exists(Scene.ArtifactLeaf(prepared.PreparedIndexRelativePath)));
    }

    [Fact]
    public async Task Pending_index_attention_is_sticky_even_after_the_unknown_lock_disappears()
    {
        var (ids, script, prepared) = await CrashAtAsync(Boundary.AfterPlanBeforeIndex);
        using (StartHost(runSupervisor: false))
        {
            Assert.Equal("local_commit.unknown_index_lock", (await OperationRowAsync(ids.RunId)).OutcomeReasonCode);
        }

        Assert.True(await script.ReleaseRetainedAsync());
        using var later = StartHost(runSupervisor: false);

        var operation = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.NeedsAttention, operation.Status);
        Assert.Equal("local_commit.index_state_unrecognized", operation.OutcomeReasonCode);
        Assert.Equal(WorkspaceStatus.NeedsAttention, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(prepared.CommitSha, BranchTip());
        Assert.Equal(2, await EventCountAsync(ids.RunId, "local_commit.needs_attention"));
    }

    public enum ForeignLock
    {
        Reference,
        Head,
    }

    private const string Sentinel = "FOREIGN-LOCK-SENTINEL\n";

    private string ForeignLockPath(ForeignLock kind) => kind == ForeignLock.Head
        ? Path.Combine(Scene.AdministrativeDirectory, "HEAD.lock")
        : Path.Combine(Scene.CommonDirectory, "refs", "heads", Scene.BranchName.Replace('/', Path.DirectorySeparatorChar) + ".lock");

    /// <summary>Places one foreign lock file, written by nothing this operation owns, in the exact namespace that the prepared
    /// reference transaction locks: the owned branch ref in the proven common directory, or HEAD in the proven linked-worktree
    /// administrative directory.</summary>
    private string PlaceForeignLock(ForeignLock kind)
    {
        var path = ForeignLockPath(kind);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Sentinel);
        return path;
    }

    [Theory]
    [InlineData(ForeignLock.Reference, Boundary.BeforeMarker)]
    [InlineData(ForeignLock.Reference, Boundary.AfterMarkerBeforeAcquire)]
    [InlineData(ForeignLock.Reference, Boundary.AfterIndexBeforeOutcome)]
    [InlineData(ForeignLock.Head, Boundary.BeforeMarker)]
    [InlineData(ForeignLock.Head, Boundary.AfterMarkerBeforeAcquire)]
    [InlineData(ForeignLock.Head, Boundary.AfterIndexBeforeOutcome)]
    public async Task A_foreign_reference_or_head_lock_keeps_restart_under_attention_and_causes_no_recovery_effect(
        ForeignLock kind, Boundary boundary)
    {
        var (ids, _, prepared) = await CrashAtAsync(boundary);
        var lockPath = PlaceForeignLock(kind);
        var tipBefore = BranchTip();
        var indexBefore = File.ReadAllBytes(Scene.IndexPath);
        var mainBefore = Scene.MainRepositoryFingerprint();
        var eventsBefore = await EventTypeRowsAsync(ids.RunId);

        using var host = StartHost(runSupervisor: false);

        var operation = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.NeedsAttention, operation.Status);
        Assert.Equal("local_commit.unknown_reference_lock", operation.OutcomeReasonCode);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
        var workspace = await WorkspaceRowAsync(ids.WorkspaceId);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
        Assert.True(workspace.IsAttentionFromLocalCommit);
        var events = await EventTypeRowsAsync(ids.RunId);
        Assert.Equal(eventsBefore.Length + 1, events.Length);
        Assert.Equal("local_commit.needs_attention", events[^1]);
        Assert.DoesNotContain("local_commit.interrupted", events);
        Assert.DoesNotContain("local_commit.completed", events);
        Assert.Equal(Sentinel, File.ReadAllText(lockPath));
        Assert.Equal(tipBefore, BranchTip());
        Assert.Equal(indexBefore, File.ReadAllBytes(Scene.IndexPath));
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        Assert.Equal(boundary == Boundary.AfterIndexBeforeOutcome ? prepared.CommitSha : Scene.BaselineCommit, tipBefore);
    }

    [Theory]
    [InlineData(ForeignLock.Reference)]
    [InlineData(ForeignLock.Head)]
    public async Task A_foreign_reference_or_head_lock_stops_a_recorded_index_promotion_plan_before_any_new_acquisition(ForeignLock kind)
    {
        var (ids, script, prepared) = await CrashAtAsync(Boundary.AfterPlanBeforeIndex);
        Assert.True(await script.ReleaseRetainedAsync());
        var lockPath = PlaceForeignLock(kind);
        var indexBefore = File.ReadAllBytes(Scene.IndexPath);

        using var host = StartHost(runSupervisor: false);

        var operation = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.NeedsAttention, operation.Status);
        Assert.Equal("local_commit.unknown_reference_lock", operation.OutcomeReasonCode);
        Assert.Equal(WorkspaceStatus.NeedsAttention, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.Equal(prepared.CommitSha, BranchTip());
        Assert.Equal(indexBefore, File.ReadAllBytes(Scene.IndexPath));
        Assert.False(File.Exists(Scene.IndexPath + ".lock"), "no new index acquisition happened");
        Assert.Equal(Sentinel, File.ReadAllText(lockPath));
        Assert.Equal(0, await EventCountAsync(ids.RunId, "local_commit.completed"));
        Assert.Equal(1, await EventCountAsync(ids.RunId, "local_commit.needs_attention"));
        Assert.True(Directory.Exists(Scene.ArtifactLeaf(prepared.PreparedIndexRelativePath)), "preparation evidence is kept while unresolved");
    }

    [Theory]
    [InlineData(Boundary.BeforeMarker, LocalCommitStatus.Interrupted, "local_commit.interrupted_before_execution")]
    [InlineData(Boundary.AfterMarkerBeforeAcquire, LocalCommitStatus.Interrupted, "local_commit.interrupted_unpromoted")]
    [InlineData(Boundary.AfterIndexBeforeOutcome, LocalCommitStatus.Completed, null)]
    public async Task Lock_free_controls_still_recover_exactly_when_the_reference_namespace_is_proven_clear(
        Boundary boundary, LocalCommitStatus expected, string? reason)
    {
        var (ids, _, _) = await CrashAtAsync(boundary);
        Assert.False(File.Exists(ForeignLockPath(ForeignLock.Reference)));
        Assert.False(File.Exists(ForeignLockPath(ForeignLock.Head)));

        using var host = StartHost(runSupervisor: false);

        var operation = await OperationRowAsync(ids.RunId);
        Assert.Equal(expected, operation.Status);
        Assert.Equal(reason, operation.OutcomeReasonCode);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
    }

    [Fact]
    public async Task A_lock_free_recorded_plan_is_still_finished_and_completed()
    {
        var (ids, script, prepared) = await CrashAtAsync(Boundary.AfterPlanBeforeIndex);
        Assert.True(await script.ReleaseRetainedAsync());

        using var host = StartHost(runSupervisor: false);

        Assert.Equal(LocalCommitStatus.Completed, (await OperationRowAsync(ids.RunId)).Status);
        Assert.Equal(prepared.CommitSha, BranchTip());
    }

    [Theory]
    [InlineData(ForeignLock.Reference)]
    [InlineData(ForeignLock.Head)]
    public async Task A_foreign_lock_that_vanishes_before_a_later_restart_lets_fresh_proof_decide_again(ForeignLock kind)
    {
        var (ids, _, _) = await CrashAtAsync(Boundary.AfterMarkerBeforeAcquire);
        var lockPath = PlaceForeignLock(kind);
        using (StartHost(runSupervisor: false))
        {
            Assert.Equal("local_commit.unknown_reference_lock", (await OperationRowAsync(ids.RunId)).OutcomeReasonCode);
        }

        File.Delete(lockPath);
        using var later = StartHost(runSupervisor: false);

        // The decision is always recomputed from fresh, positive evidence: the lock is gone, the branch, index and ownership are
        // exactly the recorded unpromoted state, so the operation is now proven interrupted.
        var operation = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.Interrupted, operation.Status);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(Scene.BaselineCommit, BranchTip());
    }

    public enum Interference
    {
        BranchTipMoved,
        HeadRebound,
        MarkerReplaced,
        IndexChanged,
        ForeignLock,
    }

    [Theory]
    [InlineData(Interference.BranchTipMoved, "local_commit.branch_tip_unrecognized")]
    [InlineData(Interference.HeadRebound, "local_commit.head_not_bound")]
    [InlineData(Interference.MarkerReplaced, "local_commit.ownership_unprovable")]
    [InlineData(Interference.IndexChanged, "local_commit.index_state_unrecognized")]
    [InlineData(Interference.ForeignLock, "local_commit.unknown_index_lock")]
    public async Task External_changes_after_a_delivered_commit_keep_restart_under_attention_and_never_complete_it(
        Interference interference, string expectedReason)
    {
        var (ids, _, prepared) = await CrashAtAsync(Boundary.AfterIndexBeforeOutcome);
        var external = string.Empty;
        switch (interference)
        {
            case Interference.BranchTipMoved:
                var tree = Scene.RunWorkspaceGit("rev-parse", "HEAD^{tree}").Trim();
                external = Scene.RunWorkspaceGit("commit-tree", tree, "-p", prepared.CommitSha, "-m", "external").Trim();
                Scene.RunWorkspaceGit("update-ref", "refs/heads/" + Scene.BranchName, external, prepared.CommitSha);
                break;
            case Interference.HeadRebound:
                Scene.RunWorkspaceGit("symbolic-ref", "HEAD", "refs/heads/elsewhere");
                break;
            case Interference.MarkerReplaced:
                var replaced = new WorkspaceOwnershipMarker(
                    ids.WorkspaceId, ids.ProjectId, Guid.NewGuid(), Scene.Ownership.PhysicalVolumeSerialNumber,
                    Scene.Ownership.PhysicalFileIdHex);
                Assert.Equal(
                    WorkspaceOwnershipMarkerWriteOutcome.Success,
                    (await Scene.Markers.WriteAsync(Scene.AdministrativeDirectory, replaced, CancellationToken.None)).Outcome);
                break;
            case Interference.IndexChanged:
                File.AppendAllBytes(Scene.IndexPath, [0]);
                break;
            case Interference.ForeignLock:
                File.WriteAllText(Scene.IndexPath + ".lock", "another process");
                break;
        }

        var mainBefore = Scene.MainRepositoryFingerprint();
        using var host = StartHost(runSupervisor: false);

        var operation = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.NeedsAttention, operation.Status);
        Assert.Equal(expectedReason, operation.OutcomeReasonCode);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
        // A replaced ownership marker is additionally an ADR-0008 external alteration, which the ordinary reconciliation that
        // follows recovery records on the workspace; every other interference keeps the local-commit attention reservation.
        Assert.Equal(
            interference == Interference.MarkerReplaced ? WorkspaceStatus.AlteredExternally : WorkspaceStatus.NeedsAttention,
            (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(0, await EventCountAsync(ids.RunId, "local_commit.completed"));
        Assert.Equal(1, await EventCountAsync(ids.RunId, "local_commit.needs_attention"));
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        if (interference == Interference.BranchTipMoved)
        {
            Assert.Equal(external, BranchTip());
        }

        if (interference == Interference.ForeignLock)
        {
            Assert.Equal("another process", File.ReadAllText(Scene.IndexPath + ".lock"));
        }
    }

    [Fact]
    public async Task A_workspace_edit_after_the_delivery_completes_the_exact_commit_but_never_labels_the_workspace_clean()
    {
        var (ids, _, prepared) = await CrashAtAsync(Boundary.AfterIndexBeforeOutcome);
        Scene.WriteWorkspace("a.txt", "edited after the commit\n");

        using var host = StartHost(runSupervisor: false);

        var operation = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.Completed, operation.Status);
        Assert.Equal(prepared.CommitSha, BranchTip());
        Assert.Equal(RunLifecycle.Completed, (await RunRowAsync(ids.RunId)).Lifecycle);
        var workspace = await WorkspaceRowAsync(ids.WorkspaceId);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
        Assert.Equal("local_commit.source_changed_after_commit", workspace.LastFailureReasonCode);
        Assert.Equal("approved change\n", Scene.RunMainGit("cat-file", "-p", prepared.CommitSha + ":a.txt").Replace("\r", string.Empty));
        Assert.Equal(1, await EventCountAsync(ids.RunId, "local_commit.completed"));
    }

    [Fact]
    public async Task A_prepared_operation_whose_state_changed_before_restart_is_attention_not_interrupted()
    {
        var (ids, _, prepared) = await CrashAtAsync(Boundary.BeforeMarker);
        var tree = Scene.RunWorkspaceGit("rev-parse", "HEAD^{tree}").Trim();
        var external = Scene.RunWorkspaceGit("commit-tree", tree, "-p", Scene.BaselineCommit, "-m", "external").Trim();
        Scene.RunWorkspaceGit("update-ref", "refs/heads/" + Scene.BranchName, external, Scene.BaselineCommit);

        using var host = StartHost(runSupervisor: false);

        var operation = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.NeedsAttention, operation.Status);
        Assert.Equal("local_commit.prepared_state_unexpected", operation.OutcomeReasonCode);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.Equal(WorkspaceStatus.NeedsAttention, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(external, BranchTip());
        Assert.NotEqual(prepared.CommitSha, BranchTip());
    }

    [Fact]
    public async Task A_broken_completed_commit_chain_fails_closed_at_restart_instead_of_trusting_the_new_tip()
    {
        var (ids, _, prepared) = await CrashAtAsync(Boundary.AfterIndexBeforeOutcome);
        using (StartHost(runSupervisor: false))
        {
            Assert.Equal(LocalCommitStatus.Completed, (await OperationRowAsync(ids.RunId)).Status);
            Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        }

        // The recorded parent edge no longer reaches the observed tip, so the chain is broken and no tip is expected.
        await ExecuteSqlAsync(
            $"UPDATE local_commit_operations SET \"ParentCommitSha\" = '{new string('9', 40)}' WHERE \"Id\" = '{prepared.Id.ToString().ToUpperInvariant()}'");

        using var broken = StartHost(runSupervisor: false);

        var workspace = await WorkspaceRowAsync(ids.WorkspaceId);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
        Assert.Equal("workspaces.reconciliation_head_diverged", workspace.LastFailureReasonCode);
    }

    [Fact]
    public async Task Failure_cleanup_releases_the_retained_capability_so_teardown_cannot_fail_a_second_time()
    {
        // A fixture whose test fails right after a simulated host loss never reaches its own explicit release. Its teardown must
        // still release the capability it deliberately retained (through the repository's own release API, after stopping the host);
        // otherwise the open handle on the prepared artifact makes the scene's teardown throw and hides the real failure.
        var probe = new LocalCommitRestartRecoveryTests();
        var (_, script, _) = await probe.CrashAtAsync(Boundary.AfterAcquireBeforeRef);
        var sceneRoot = probe.Scene.Root;
        Assert.True(File.Exists(probe.Scene.IndexPath + ".lock"), "the fixture holds the live lock capability");
        Assert.Equal(0, script.OutstandingEffects);

        var teardown = Record.Exception(probe.Dispose);

        Assert.Null(teardown);
        Assert.False(Directory.Exists(sceneRoot), "the scene was removed completely");
    }

    [Fact]
    public async Task Releasing_the_retained_capability_releases_only_what_the_fixture_owns()
    {
        var (_, script, _) = await CrashAtAsync(Boundary.AfterAcquireBeforeRef);
        var foreignReference = PlaceForeignLock(ForeignLock.Reference);
        var indexLock = Scene.IndexPath + ".lock";
        Assert.True(File.Exists(indexLock));

        // The owned capability goes through its own handle; the foreign reference lock beside it is left exactly as it was.
        Assert.True(await script.ReleaseRetainedAsync());
        Assert.False(File.Exists(indexLock));
        Assert.Equal(Sentinel, File.ReadAllText(foreignReference));

        // Once released the capability is not held any more: a second release is refused, and a lock that appears at the same
        // pathname afterwards is foreign and is never adopted or removed (the fixture teardown calls the same API once more).
        File.WriteAllText(indexLock, "another process");
        Assert.False(await script.ReleaseRetainedAsync());
        Assert.Equal("another process", File.ReadAllText(indexLock));
        Assert.Equal(Sentinel, File.ReadAllText(foreignReference));
    }
}
