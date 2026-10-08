using System.Net;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.ExecuteLocalCommit;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0029 R4 through the real file-backed host: a host that merely declines to mutate is not proof that nothing external
/// changed. The reservation and the nonterminal run are released only from a freshly proven unpromoted, still-owned, unchanged
/// state; every external interference keeps the operation under attention with the interfering state intact.
/// </summary>
public sealed class LocalCommitSafeReleaseTests : LocalCommitTestBase
{
    public enum Change
    {
        BranchTipMoved,
        HeadRebound,
        RealIndexChanged,
        ForeignLock,
        MarkerReplaced,
    }

    private async Task ApplyAsync(Change change, LocalCommitLineageIds ids)
    {
        switch (change)
        {
            case Change.BranchTipMoved:
                var tree = Scene.RunWorkspaceGit("rev-parse", "HEAD^{tree}").Trim();
                var external = Scene.RunWorkspaceGit("commit-tree", tree, "-p", Scene.BaselineCommit, "-m", "external").Trim();
                Scene.RunWorkspaceGit("update-ref", "refs/heads/" + Scene.BranchName, external, Scene.BaselineCommit);
                break;
            case Change.HeadRebound:
                Scene.RunWorkspaceGit("symbolic-ref", "HEAD", "refs/heads/elsewhere");
                break;
            case Change.RealIndexChanged:
                Scene.RunWorkspaceGit("add", "a.txt");
                break;
            case Change.ForeignLock:
                File.WriteAllText(Scene.IndexPath + ".lock", "another process");
                break;
            case Change.MarkerReplaced:
                var marker = new WorkspaceOwnershipMarker(
                    ids.WorkspaceId, ids.ProjectId, Guid.NewGuid(), Scene.Ownership.PhysicalVolumeSerialNumber,
                    Scene.Ownership.PhysicalFileIdHex);
                Assert.Equal(
                    WorkspaceOwnershipMarkerWriteOutcome.Success,
                    (await Scene.Markers.WriteAsync(Scene.AdministrativeDirectory, marker, CancellationToken.None)).Outcome);
                break;
        }
    }

    private async Task<LocalCommitOperation> WaitForOutcomeAsync(Guid runId)
    {
        LocalCommitOperation? operation = null;
        await WaitUntilAsync(async () =>
        {
            operation = await OperationRowAsync(runId);
            return operation.Status is LocalCommitStatus.Completed or LocalCommitStatus.Failed or LocalCommitStatus.NeedsAttention
                or LocalCommitStatus.Interrupted;
        });
        return operation!;
    }

    [Theory]
    [InlineData(Change.BranchTipMoved)]
    [InlineData(Change.HeadRebound)]
    [InlineData(Change.RealIndexChanged)]
    [InlineData(Change.ForeignLock)]
    [InlineData(Change.MarkerReplaced)]
    public async Task External_interference_before_acquisition_is_never_released_as_ready_or_failed(Change change)
    {
        var (host, script) = StartScripted(supervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        script.BeforeAcquire = async _ => await ApplyAsync(change, ids);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);

        var operation = await WaitForOutcomeAsync(ids.RunId);

        Assert.Equal(LocalCommitStatus.NeedsAttention, operation.Status);
        Assert.Equal("local_commit.release_unproven", operation.OutcomeReasonCode);
        var workspace = await WorkspaceRowAsync(ids.WorkspaceId);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
        Assert.True(workspace.IsAttentionFromLocalCommit);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.Equal(
            ["local_commit.admitted", "local_commit.executing", "local_commit.needs_attention"],
            (await EventTypeRowsAsync(ids.RunId)).Where(type => type.StartsWith("local_commit.", StringComparison.Ordinal)));
        Assert.DoesNotContain(nameof(ILocalCommitRepository.PromoteRefAsync), script.Calls);
        if (change == Change.ForeignLock)
        {
            Assert.Equal("another process", File.ReadAllText(Scene.IndexPath + ".lock"));
        }

        if (change == Change.BranchTipMoved)
        {
            Assert.NotEqual(Scene.BaselineCommit, BranchTip());
            Assert.NotEqual(operation.CommitSha, BranchTip());
        }
    }

    [Fact]
    public async Task A_definitely_unpromoted_failure_with_proven_state_fails_the_run_and_releases_the_reservation()
    {
        var (host, script) = StartScripted(supervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var mainBefore = Scene.MainRepositoryFingerprint();
        script.BeforeAcquire = facts =>
        {
            // Only the host-owned prepared artifact is damaged; branch, HEAD, index, lock and ownership stay exactly as proven.
            File.AppendAllText(Scene.Storage.ResolveArtifact(facts.PreparedIndexRelativePath)!, "tamper");
            return Task.CompletedTask;
        };
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);

        var operation = await WaitForOutcomeAsync(ids.RunId);

        Assert.Equal(LocalCommitStatus.Failed, operation.Status);
        Assert.Equal("local_commit.index_acquisition_unproven", operation.OutcomeReasonCode);
        Assert.Equal(RunLifecycle.Failed, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(Scene.BaselineCommit, BranchTip());
        Assert.False(File.Exists(Scene.IndexPath + ".lock"));
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        Assert.Equal(
            ["local_commit.admitted", "local_commit.executing", "local_commit.failed"],
            (await EventTypeRowsAsync(ids.RunId)).Where(type => type.StartsWith("local_commit.", StringComparison.Ordinal)));
        Assert.False(Directory.Exists(Scene.ArtifactLeaf(operation.PreparedIndexRelativePath)));

        // A failed operation never authorizes another commit for that run.
        var again = await PostAsync(host, ids.RunId, ids);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_branch_that_moved_between_acquisition_and_the_reference_effect_is_attention_with_the_lock_released()
    {
        var (host, script) = StartScripted(supervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        string external = string.Empty;
        script.BeforePromoteRef = _ =>
        {
            var tree = Scene.RunWorkspaceGit("rev-parse", "HEAD^{tree}").Trim();
            external = Scene.RunWorkspaceGit("commit-tree", tree, "-p", Scene.BaselineCommit, "-m", "external").Trim();
            Scene.RunWorkspaceGit("update-ref", "refs/heads/" + Scene.BranchName, external, Scene.BaselineCommit);
            return Task.CompletedTask;
        };
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);

        var operation = await WaitForOutcomeAsync(ids.RunId);

        Assert.Equal(LocalCommitStatus.NeedsAttention, operation.Status);
        Assert.Equal("local_commit.release_unproven", operation.OutcomeReasonCode);
        Assert.Equal(WorkspaceStatus.NeedsAttention, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.Equal(external, BranchTip());
        Assert.False(File.Exists(Scene.IndexPath + ".lock"), "the host's own lock was released by handle");
    }

    [Fact]
    public async Task A_head_rebinding_attempt_during_the_reference_effect_is_refused_and_the_delivery_completes()
    {
        var (host, script) = StartScripted(supervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var refused = -1;
        script.BeforePromoteRef = _ =>
        {
            refused = Scene.RunGitRaw(Scene.WorkspacePath, "symbolic-ref", "HEAD", "refs/heads/elsewhere").ExitCode;
            return Task.CompletedTask;
        };
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);

        var operation = await WaitForOutcomeAsync(ids.RunId);

        Assert.NotEqual(0, refused);
        Assert.Equal(LocalCommitStatus.Completed, operation.Status);
        Assert.Equal("refs/heads/" + Scene.BranchName, Scene.RunWorkspaceGit("symbolic-ref", "HEAD").Trim());
    }

    public enum ForeignReferenceNamespaceLock
    {
        Reference,
        Head,
    }

    [Theory]
    [InlineData(ForeignReferenceNamespaceLock.Reference)]
    [InlineData(ForeignReferenceNamespaceLock.Head)]
    public async Task A_foreign_reference_or_head_lock_that_refuses_the_prepared_transaction_is_never_released_as_failed(
        ForeignReferenceNamespaceLock kind)
    {
        var (host, script) = StartScripted(supervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var lockPath = kind == ForeignReferenceNamespaceLock.Head
            ? Path.Combine(Scene.AdministrativeDirectory, "HEAD.lock")
            : Path.Combine(Scene.CommonDirectory, "refs", "heads", Scene.BranchName.Replace('/', Path.DirectorySeparatorChar) + ".lock");
        script.BeforePromoteRef = _ =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            File.WriteAllText(lockPath, "FOREIGN-LOCK-SENTINEL\n");
            return Task.CompletedTask;
        };
        var mainBefore = Scene.MainRepositoryFingerprint();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);

        var operation = await WaitForOutcomeAsync(ids.RunId);

        // Git itself refuses the transaction (it cannot lock the ref), so the reference never moved; but a foreign lock means another
        // process may be mid-update, so "not promoted" is not enough to release the reservation or fail the run.
        Assert.Contains(nameof(ILocalCommitRepository.PromoteRefAsync), script.Calls);
        Assert.Equal(LocalCommitStatus.NeedsAttention, operation.Status);
        Assert.Equal("local_commit.release_unproven", operation.OutcomeReasonCode);
        var workspace = await WorkspaceRowAsync(ids.WorkspaceId);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
        Assert.True(workspace.IsAttentionFromLocalCommit);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.Equal(
            ["local_commit.admitted", "local_commit.executing", "local_commit.needs_attention"],
            (await EventTypeRowsAsync(ids.RunId)).Where(type => type.StartsWith("local_commit.", StringComparison.Ordinal)));
        Assert.Equal("FOREIGN-LOCK-SENTINEL\n", File.ReadAllText(lockPath));
        Assert.Equal(Scene.BaselineCommit, BranchTip());
        Assert.False(File.Exists(Scene.IndexPath + ".lock"), "the host's own index lock was released by handle");
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        Assert.True(Directory.Exists(Scene.ArtifactLeaf(operation.PreparedIndexRelativePath)), "evidence is kept while unresolved");
    }

    public enum Refusal
    {
        HumanDecisionChanged,
        RunNoLongerRunning,
        DigestOnlyChange,
    }

    [Theory]
    [InlineData(Refusal.HumanDecisionChanged, false)]
    [InlineData(Refusal.RunNoLongerRunning, false)]
    [InlineData(Refusal.DigestOnlyChange, false)]
    [InlineData(Refusal.HumanDecisionChanged, true)]
    public async Task An_authority_change_after_admission_refuses_execution_and_still_proves_release_first(
        Refusal refusal, bool alsoMovedBranch)
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);
        var admitted = await OperationRowAsync(ids.RunId);
        await ExecuteSqlAsync(refusal switch
        {
            Refusal.HumanDecisionChanged =>
                $"UPDATE checkpoint_reviews SET \"Decision\" = 'ChangesRequested' WHERE \"Id\" = '{ids.HumanReviewId.ToString().ToUpperInvariant()}'",
            Refusal.DigestOnlyChange =>
                $"UPDATE verification_executions SET \"ExecutionNumber\" = 99 WHERE \"Id\" = '{ids.ExecutionId.ToString().ToUpperInvariant()}'",
            _ =>
                $"UPDATE runs SET \"Lifecycle\" = 'Failed' WHERE \"Id\" = '{ids.RunId.ToString().ToUpperInvariant()}'",
        });
        if (alsoMovedBranch)
        {
            var tree = Scene.RunWorkspaceGit("rev-parse", "HEAD^{tree}").Trim();
            var external = Scene.RunWorkspaceGit("commit-tree", tree, "-p", Scene.BaselineCommit, "-m", "external").Trim();
            Scene.RunWorkspaceGit("update-ref", "refs/heads/" + Scene.BranchName, external, Scene.BaselineCommit);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var result = await mediator.SendAsync(new ExecuteLocalCommitCommand(admitted.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var operation = await OperationRowAsync(ids.RunId);
        var events = (await EventTypeRowsAsync(ids.RunId)).Where(type => type.StartsWith("local_commit.", StringComparison.Ordinal)).ToArray();
        Assert.DoesNotContain("local_commit.executing", events);
        if (alsoMovedBranch)
        {
            Assert.Equal(LocalCommitStatus.NeedsAttention, operation.Status);
            Assert.Equal("local_commit.release_unproven", operation.OutcomeReasonCode);
            Assert.Equal(WorkspaceStatus.NeedsAttention, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
            Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
            Assert.Equal(["local_commit.admitted", "local_commit.needs_attention"], events);
            Assert.True(Directory.Exists(Scene.ArtifactLeaf(operation.PreparedIndexRelativePath)), "evidence is kept while unresolved");
        }
        else
        {
            Assert.Equal(LocalCommitStatus.Failed, operation.Status);
            Assert.Equal("local_commit.authority_changed", operation.OutcomeReasonCode);
            Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
            Assert.Equal(["local_commit.admitted", "local_commit.failed"], events);
            Assert.False(Directory.Exists(Scene.ArtifactLeaf(operation.PreparedIndexRelativePath)));
            Assert.Equal(Scene.BaselineCommit, BranchTip());
        }

        // A proven-safe refusal fails the run (or leaves the externally failed run as it is); an unproven one never does.
        Assert.Equal(alsoMovedBranch ? RunLifecycle.Running : RunLifecycle.Failed, (await RunRowAsync(ids.RunId)).Lifecycle);
    }
}
