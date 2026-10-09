using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Api.Features.Projects.GetProjectRunSummaries;
using DevalCopilot.Api.Features.Runs.AbandonManualRun;
using DevalCopilot.Api.Features.Runs.CreateManualRun;
using DevalCopilot.Api.Features.Runs.GetManualRunAbandonment;
using DevalCopilot.Api.Features.Runs.GetRunCockpit;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0031 against the production host, real Git and real SQLite: how an explicit abandonment meets the explicit local commit in BOTH
/// directions, and what it leaves alone. A reserved workspace or an open, even ambiguous, operation makes the abandonment refuse with
/// nothing written; an abandonment that wins before the locked admission refuses the admission and the reservation rolls back; an
/// abandoned run leaves the workspace, lease, branch, index and source bytes exactly as they were, and the abandonment, its reason and
/// the next objective survive a restart. A verification claimed after the closure remains project work, not a resumed run.
/// </summary>
public sealed class LocalCommitAbandonmentTests : LocalCommitTestBase
{
    private const string Reason = "The change will be delivered another way.";

    private static async Task<HttpResponseMessage> AbandonAsync(LocalCommitHost host, Guid runId, string reason = Reason)
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        return await client.PostAsJsonAsync($"/api/runs/{runId}/abandon", new AbandonManualRunRequest { Reason = reason });
    }

    private static async Task<GetManualRunAbandonmentResponse> AbandonmentStatusAsync(LocalCommitHost host, Guid runId)
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var response = await client.GetAsync($"/api/runs/{runId}/abandonment");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GetManualRunAbandonmentResponse>())!;
    }

    /// <summary>A digest of every byte an abandonment must not touch: the owned workspace files, the administrative index, the branch tip
    /// and the main checkout.</summary>
    private string SourceDigest()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(Scene.WorkspacePath, "*", SearchOption.AllDirectories)
                     .Where(path => !Path.GetFileName(path).Equals(".git", StringComparison.Ordinal))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(Scene.WorkspacePath, file) + "\n"));
            hash.AppendData(File.ReadAllBytes(file));
        }

        hash.AppendData(File.ReadAllBytes(Scene.IndexPath));
        hash.AppendData(Encoding.UTF8.GetBytes(BranchTip() + Scene.MainRepositoryFingerprint()));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // ---- the abandonment meets an admitted or ambiguous operation -----------------------------------------------------------------

    [Fact]
    public async Task An_admitted_operation_and_its_reserved_workspace_make_the_abandonment_refuse_and_persist_nothing()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);
        Assert.Equal(WorkspaceStatus.Committing, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        var eventsBefore = await EventTypeRowsAsync(ids.RunId);
        var digest = SourceDigest();

        var response = await AbandonAsync(host, ids.RunId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("run_abandonment.local_commit_open", await ErrorCodeAsync(response));
        var status = await AbandonmentStatusAsync(host, ids.RunId);
        Assert.False(status.Eligible);
        Assert.Equal("run_abandonment.local_commit_open", status.RefusalCode);
        await AssertReservedAsync(ids, WorkspaceStatus.Committing, LocalCommitStatus.Prepared);
        Assert.Equal(eventsBefore, await EventTypeRowsAsync(ids.RunId));
        Assert.Null((await RunRowAsync(ids.RunId)).AbandonmentReason);
        Assert.Equal(digest, SourceDigest());
    }

    [Fact]
    public async Task An_ambiguous_operation_that_needs_attention_is_never_overridden_by_abandoning_its_run()
    {
        var (host, script) = StartScripted(supervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        script.BeforeAcquire = _ =>
        {
            File.WriteAllText(Scene.IndexPath + ".lock", "another process");
            return Task.CompletedTask;
        };
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);
        await WaitUntilAsync(async () => (await OperationRowAsync(ids.RunId)).Status == LocalCommitStatus.NeedsAttention);
        var eventsBefore = await EventTypeRowsAsync(ids.RunId);

        var response = await AbandonAsync(host, ids.RunId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("run_abandonment.local_commit_open", await ErrorCodeAsync(response));
        await AssertReservedAsync(ids, WorkspaceStatus.NeedsAttention, LocalCommitStatus.NeedsAttention);
        Assert.Equal(eventsBefore, await EventTypeRowsAsync(ids.RunId));
        Assert.Equal("another process", File.ReadAllText(Scene.IndexPath + ".lock"));
        Assert.Null((await RunRowAsync(ids.RunId)).AbandonmentReason);
    }

    // ---- the abandonment wins the race with admission --------------------------------------------------------------------------------

    [Fact]
    public async Task An_abandonment_that_commits_after_preparation_refuses_the_locked_admission_and_the_reservation_rolls_back()
    {
        var preparer = new ScriptedLocalCommitPreparer();
        using var host = StartHost(runSupervisor: false, decoratePreparer: inner => preparer.Attach(inner));
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var digest = SourceDigest();
        HttpResponseMessage? abandonment = null;
        preparer.AfterPrepare = async () => abandonment = await AbandonAsync(host, ids.RunId);

        var response = await PostAsync(host, ids.RunId, ids);

        Assert.NotNull(abandonment);
        Assert.Equal(HttpStatusCode.OK, abandonment.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("local_commit.run_not_eligible", await ErrorCodeAsync(response));
        Assert.Equal(0, await OperationCountAsync(host));
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        var run = await RunRowAsync(ids.RunId);
        Assert.Equal(RunLifecycle.Abandoned, run.Lifecycle);
        var events = await EventTypeRowsAsync(ids.RunId);
        Assert.Single(events, type => type == RunEventType.RunAbandoned);
        Assert.DoesNotContain(events, type => type.StartsWith("local_commit.", StringComparison.Ordinal));
        Assert.Equal(digest, SourceDigest());
    }

    [Fact]
    public async Task An_admission_that_commits_first_wins_so_a_later_abandonment_is_the_one_refused()
    {
        var preparer = new ScriptedLocalCommitPreparer();
        using var host = StartHost(runSupervisor: false, decoratePreparer: inner => preparer.Attach(inner));
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        HttpResponseMessage? abandonment = null;
        var admitted = await PostAsync(host, ids.RunId, ids);
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);

        abandonment = await AbandonAsync(host, ids.RunId);

        Assert.Equal(HttpStatusCode.Conflict, abandonment.StatusCode);
        Assert.Equal(1, await OperationCountAsync(host));
        await AssertReservedAsync(ids, WorkspaceStatus.Committing, LocalCommitStatus.Prepared);
    }

    // ---- what an abandonment leaves alone --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Abandoning_a_run_with_a_finished_lineage_leaves_the_workspace_lease_branch_index_and_source_bytes_unchanged()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var digest = SourceDigest();
        var workspaceBefore = await WorkspaceRowAsync(ids.WorkspaceId);
        var leaseBefore = await WithDbAsync(host, db => db.RepositoryMutationLeases.AsNoTracking().SingleAsync(lease => lease.Id == ids.LeaseId));
        var checkpointsBefore = await WithDbAsync(host, db => db.GitCheckpoints.AsNoTracking().CountAsync(c => c.WorkspaceId == ids.WorkspaceId));
        var attemptsBefore = await WithDbAsync(host, db => db.Attempts.AsNoTracking().CountAsync(a => a.RunId == ids.RunId));
        var messagesBefore = await WithDbAsync(host, db => db.CollaborationMessages.AsNoTracking().CountAsync(m => m.RunId == ids.RunId));
        var reviewsBefore = await WithDbAsync(host, db => db.CheckpointReviews.AsNoTracking().CountAsync(r => r.ProjectId == ids.ProjectId));

        var response = await AbandonAsync(host, ids.RunId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(digest, SourceDigest());
        var workspaceAfter = await WorkspaceRowAsync(ids.WorkspaceId);
        Assert.Equal(workspaceBefore.Status, workspaceAfter.Status);
        Assert.Equal(workspaceBefore.WorkspacePath, workspaceAfter.WorkspacePath);
        var leaseAfter = await WithDbAsync(host, db => db.RepositoryMutationLeases.AsNoTracking().SingleAsync(lease => lease.Id == ids.LeaseId));
        Assert.Equal(leaseBefore.Status, leaseAfter.Status);
        Assert.Equal(LeaseStatus.Active, leaseAfter.Status);
        Assert.Equal(checkpointsBefore, await WithDbAsync(host, db => db.GitCheckpoints.AsNoTracking().CountAsync(c => c.WorkspaceId == ids.WorkspaceId)));
        Assert.Equal(attemptsBefore, await WithDbAsync(host, db => db.Attempts.AsNoTracking().CountAsync(a => a.RunId == ids.RunId)));
        Assert.Equal(messagesBefore, await WithDbAsync(host, db => db.CollaborationMessages.AsNoTracking().CountAsync(m => m.RunId == ids.RunId)));
        Assert.Equal(reviewsBefore, await WithDbAsync(host, db => db.CheckpointReviews.AsNoTracking().CountAsync(r => r.ProjectId == ids.ProjectId)));
        Assert.Equal(0, await OperationCountAsync(host));
    }

    [Fact]
    public async Task An_abandoned_run_refuses_the_local_commit_and_the_status_names_it_without_Git_work()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        Assert.Equal(HttpStatusCode.OK, (await AbandonAsync(host, ids.RunId)).StatusCode);

        var response = await PostAsync(host, ids.RunId, ids);
        var status = await StatusAsync(host, ids.RunId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("local_commit.run_not_eligible", await ErrorCodeAsync(response));
        Assert.False(status.Eligible);
        Assert.Equal("local_commit.run_not_eligible", status.RefusalCode);
        Assert.Equal(0, await OperationCountAsync(host));
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
    }

    // ---- restart --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_abandonment_its_reason_and_the_next_objective_survive_a_restart_with_the_workspace_untouched()
    {
        Guid projectId;
        Guid runId;
        string digest;
        AbandonManualRunResponse recorded;
        using (var first = StartHost(runSupervisor: false))
        {
            var ids = await LocalCommitLineage.SeedAsync(first, Scene);
            projectId = ids.ProjectId;
            runId = ids.RunId;
            digest = SourceDigest();
            var response = await AbandonAsync(first, runId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            recorded = (await response.Content.ReadFromJsonAsync<AbandonManualRunResponse>())!;
        }

        using var restarted = StartHost(runSupervisor: true);
        using var client = LocalCommitLineage.AuthenticatedClient(restarted);

        var status = await AbandonmentStatusAsync(restarted, runId);
        var cockpit = (await client.GetFromJsonAsync<GetRunCockpitResponse>($"/api/runs/{runId}/cockpit"))!;
        var summaries = (await client.GetFromJsonAsync<List<ProjectRunSummaryResponse>>("/api/projects/run-summaries"))!;
        var replay = await AbandonAsync(restarted, runId);
        var conflict = await AbandonAsync(restarted, runId, "A different reason");
        var next = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(projectId, "The next objective"));

        Assert.Equal(Reason, status.Abandonment!.Reason);
        Assert.Equal(recorded.AbandonedAtUtc, status.Abandonment.AbandonedAtUtc);
        Assert.Equal("Abandoned", cockpit.Lifecycle);
        Assert.Equal("Abandoned", summaries.Single(summary => summary.ProjectId == projectId).Lifecycle);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(recorded, await replay.Content.ReadFromJsonAsync<AbandonManualRunResponse>());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal(2, (await next.Content.ReadFromJsonAsync<CreateManualRunResponse>())!.ExecutionNumber);
        Assert.Equal(digest, SourceDigest());
        Assert.Single((await EventTypeRowsAsync(runId)), type => type == RunEventType.RunAbandoned);
    }

    // ---- verification seam controls ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_verification_claimed_before_the_abandonment_blocks_it_and_one_claimed_after_remains_project_work()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var claimBefore = await client.PostAsJsonAsync(
            $"/api/projects/{ids.ProjectId}/verification-commands/{ids.CommandId}/executions", new { gitCheckpointId = ids.CheckpointId });
        Assert.True(claimBefore.IsSuccessStatusCode, await claimBefore.Content.ReadAsStringAsync());

        var blocked = await AbandonAsync(host, ids.RunId);

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("run_abandonment.active_verification", await ErrorCodeAsync(blocked));
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);

        // The claimed execution ends (a project fact the abandonment never touches), the run is abandoned, and a new claim is project
        // work under its ordinary rules: it neither needs nor resumes the abandoned run.
        await ExecuteSqlAsync("UPDATE verification_executions SET Status = 'Cancelled' WHERE Status = 'Running'");
        Assert.Equal(HttpStatusCode.OK, (await AbandonAsync(host, ids.RunId)).StatusCode);
        var claimAfter = await client.PostAsJsonAsync(
            $"/api/projects/{ids.ProjectId}/verification-commands/{ids.CommandId}/executions", new { gitCheckpointId = ids.CheckpointId });

        Assert.True(claimAfter.IsSuccessStatusCode, await claimAfter.Content.ReadAsStringAsync());
        var run = await RunRowAsync(ids.RunId);
        Assert.Equal(RunLifecycle.Abandoned, run.Lifecycle);
        Assert.Equal(Reason, run.AbandonmentReason);
        Assert.Equal(
            2, await WithDbAsync(host, db => db.VerificationExecutions.AsNoTracking().CountAsync(execution => execution.ProjectId == ids.ProjectId && execution.Status != VerificationExecutionStatus.Passed)));
    }
}
