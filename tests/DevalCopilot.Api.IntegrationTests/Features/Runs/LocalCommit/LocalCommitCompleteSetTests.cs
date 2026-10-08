using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.Features.Projects.ConfigureVerificationCommand;
using DevalCopilot.Api.Features.Projects.RecordCheckpointReview;
using DevalCopilot.Api.Features.Projects.UpdateVerificationCommand;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0030 through the production host: two enabled recipes, the real CodeReviewer approval written by the production handlers, one
/// complete Human approval obtained from the protected approval bundle, and the existing explicit local commit, with real Git and
/// file-backed SQLite. The same lineage with the legacy single-member human approval stays refused, so these cases fail without the
/// complete-set contract.
/// </summary>
public sealed class LocalCommitCompleteSetTests : LocalCommitTestBase
{
    private static string Upper(Guid id) => id.ToString().ToUpperInvariant();

    [Fact]
    public async Task Two_enabled_recipes_with_one_complete_human_approval_commit_locally_and_survive_a_restart()
    {
        var mainBefore = Scene.MainRepositoryFingerprint();
        LocalCommitLineageIds ids;
        Guid operationId;
        using (var host = StartHost(runSupervisor: true))
        {
            ids = await LocalCommitLineage.SeedAsync(host, Scene, recipes: 2, completeSetHumanApproval: true);
            var status = await StatusAsync(host, ids.RunId);
            Assert.True(status.Eligible, status.RefusalCode);
            Assert.Equal(ids.HumanReviewId, status.HumanCheckpointReviewId);

            operationId = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids, operationId)).StatusCode);
            var done = await WaitForAsync(host, ids.RunId, current => current.Operation?.Status == "Completed");
            Assert.Equal(Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim(), done.Operation!.CommitSha);
            Assert.Equal(RunLifecycle.Completed, (await RunAsync(host, ids.RunId)).Lifecycle);
        }

        // Restart: a second host over the same database file and the same repository sees the identical persisted facts.
        using var restarted = StartHost(runSupervisor: true);
        var after = await StatusAsync(restarted, ids.RunId);
        Assert.Equal("Completed", after.Operation?.Status);
        Assert.Equal(Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim(), after.Operation!.CommitSha);
        Assert.Equal(RunLifecycle.Completed, (await RunAsync(restarted, ids.RunId)).Lifecycle);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceAsync(restarted, ids.WorkspaceId)).Status);
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());

        await using var db = OpenDb();
        var human = await db.CheckpointReviews.AsNoTracking().Include(review => review.Evidence).SingleAsync(review => review.Id == ids.HumanReviewId);
        Assert.Equal(ReviewActorKind.Human, human.ActorKind);
        Assert.Equal(ReviewDecision.Approved, human.Decision);
        Assert.Equal(ids.AllExecutionIds!.ToHashSet(), human.Evidence.Select(member => member.VerificationExecutionId).ToHashSet());
        var members = await db.LocalCommitAuthorityMembers.AsNoTracking()
            .Where(member => member.OperationId == operationId && member.Kind == LocalCommitAuthorityMemberKind.Verification)
            .OrderBy(member => member.Sequence).ToListAsync();
        Assert.Equal(ids.AllExecutionIds!, members.Select(member => member.SubjectId));
        Assert.Equal(2, members.Select(member => member.CommandId).Distinct().Count());
    }

    [Fact]
    public async Task A_recipe_whose_latest_execution_left_the_history_window_is_still_in_the_approved_bundle_and_the_commit()
    {
        using var host = StartHost(runSupervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene, recipes: 2, completeSetHumanApproval: true, laterRunsOfFirstRecipe: 25);

        using var client = LocalCommitLineage.AuthenticatedClient(host);
        using var history = JsonDocument.Parse(await client.GetStringAsync($"/api/projects/{ids.ProjectId}/verification-executions"));
        var window = history.RootElement.EnumerateArray().Select(item => item.GetProperty("verificationExecutionId").GetGuid()).ToList();
        Assert.Equal(20, window.Count);
        Assert.DoesNotContain(ids.AllExecutionIds![1], window);

        var status = await StatusAsync(host, ids.RunId);
        Assert.True(status.Eligible, status.RefusalCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);
        await WaitForAsync(host, ids.RunId, current => current.Operation?.Status == "Completed");
    }

    [Fact]
    public async Task The_legacy_single_member_human_approval_of_two_recipes_stays_refused_and_gains_no_authority()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene, recipes: 2);

        var status = await StatusAsync(host, ids.RunId);
        Assert.False(status.Eligible);
        Assert.Equal("local_commit.membership_mismatch", status.RefusalCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(host, ids.RunId, ids)).StatusCode);
        Assert.Equal(0, await OperationCountAsync(host));
    }

    [Fact]
    public async Task A_complete_approval_for_one_recipe_equals_the_legacy_one_recipe_delivery()
    {
        using var host = StartHost(runSupervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene, recipes: 1, completeSetHumanApproval: true);

        Assert.True((await StatusAsync(host, ids.RunId)).Eligible);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);
        await WaitForAsync(host, ids.RunId, current => current.Operation?.Status == "Completed");
    }

    [Fact]
    public async Task Combining_a_set_approval_with_a_later_human_changes_requested_still_blocks_delivery()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene, recipes: 2, completeSetHumanApproval: true);
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{ids.ProjectId}/reviews",
            new RecordCheckpointReviewRequest(ids.CheckpointId, null, "Human", "ChangesRequested", ids.AllExecutionIds));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var refused = await PostAsync(host, ids.RunId, ids);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("local_commit.human_decision_not_approved", await ErrorCodeAsync(refused));
        Assert.Equal(0, await OperationCountAsync(host));
    }

    [Fact]
    public async Task A_pending_set_review_still_blocks_delivery_as_a_non_approved_human_decision()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene, recipes: 2, completeSetHumanApproval: true);
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{ids.ProjectId}/reviews",
            new RecordCheckpointReviewRequest(ids.CheckpointId, null, "Human", "Pending", []));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var refused = await PostAsync(host, ids.RunId, ids);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("local_commit.human_decision_not_approved", await ErrorCodeAsync(refused));
    }

    [Theory]
    [InlineData("UPDATE verification_executions SET \"Status\" = 'Failed' WHERE \"Id\" = '{second}'", "local_commit.verification_not_current")]
    [InlineData("UPDATE verification_executions SET \"CommandName\" = 'tampered' WHERE \"Id\" = '{second}'", "local_commit.verification_not_current")]
    [InlineData("UPDATE verification_executions SET \"CompletionFingerprintSha256\" = '{fingerprint}' WHERE \"Id\" = '{second}'", "local_commit.verification_not_current")]
    [InlineData("UPDATE verification_executions SET \"Status\" = 'Running' WHERE \"Id\" = '{second}'", "local_commit.active_work")]
    [InlineData("DELETE FROM checkpoint_review_evidence WHERE \"VerificationExecutionId\" = '{second}' AND \"CheckpointReviewId\" = '{human}'", "local_commit.membership_mismatch")]
    public async Task A_change_to_the_second_member_of_the_complete_set_refuses_with_its_own_stable_code(string sql, string code)
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene, recipes: 2, completeSetHumanApproval: true);
        await ExecuteSqlAsync(sql
            .Replace("{second}", Upper(ids.AllExecutionIds![1]), StringComparison.Ordinal)
            .Replace("{human}", Upper(ids.HumanReviewId), StringComparison.Ordinal)
            .Replace("{fingerprint}", new string('f', 64), StringComparison.Ordinal));

        var response = await PostAsync(host, ids.RunId, ids);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, await ErrorCodeAsync(response));
        Assert.Equal(0, await OperationCountAsync(host));
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
    }

    [Fact]
    public async Task A_recipe_disabled_after_the_complete_approval_changes_the_membership_the_reviews_claimed()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene, recipes: 2, completeSetHumanApproval: true);
        await using (var db = OpenDb())
        {
            var second = await db.VerificationCommands.AsNoTracking()
                .Where(command => command.ProjectId == ids.ProjectId && command.Id != ids.CommandId).SingleAsync();
            using var client = LocalCommitLineage.AuthenticatedClient(host);
            var response = await client.PutAsJsonAsync(
                $"/api/projects/{ids.ProjectId}/verification-commands/{second.Id}",
                new UpdateVerificationCommandRequest(second.Name, second.ExecutablePath, second.Arguments, second.TimeoutSeconds, false));
            Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString());
        }

        var refused = await PostAsync(host, ids.RunId, ids);

        Assert.Equal("local_commit.membership_mismatch", await ErrorCodeAsync(refused));
    }

    [Fact]
    public async Task A_second_approved_complete_set_review_after_admission_is_a_changed_authority_at_the_locked_seam()
    {
        var preparer = new ScriptedLocalCommitPreparer();
        using var host = StartHost(runSupervisor: false, decoratePreparer: inner => preparer.Attach(inner));
        var ids = await LocalCommitLineage.SeedAsync(host, Scene, recipes: 2, completeSetHumanApproval: true);
        preparer.AfterPrepare = async () =>
        {
            await LocalCommitLineage.RecordCompleteHumanApprovalAsync(host, ids.ProjectId, ids.CheckpointId);
        };

        var response = await PostAsync(host, ids.RunId, ids);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("local_commit.authority_changed", await ErrorCodeAsync(response));
        Assert.Equal(0, await OperationCountAsync(host));
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
    }

    [Fact]
    public async Task While_the_workspace_is_reserved_a_complete_set_review_and_a_new_recipe_are_refused_and_persist_nothing()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene, recipes: 2, completeSetHumanApproval: true);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);
        Assert.Equal(WorkspaceStatus.Committing, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        await using var before = OpenDb();
        var reviewsBefore = await before.CheckpointReviews.AsNoTracking().CountAsync();
        var membersBefore = await before.CheckpointReviewEvidence.AsNoTracking().CountAsync();
        using var client = LocalCommitLineage.AuthenticatedClient(host);

        var review = await client.PostAsJsonAsync(
            $"/api/projects/{ids.ProjectId}/reviews",
            new RecordCheckpointReviewRequest(ids.CheckpointId, null, "Human", "Approved", ids.AllExecutionIds));
        var bundle = await client.GetAsync($"/api/projects/{ids.ProjectId}/checkpoints/{ids.CheckpointId}/approval-evidence");
        var recipe = await client.PostAsJsonAsync(
            $"/api/projects/{ids.ProjectId}/verification-commands",
            new ConfigureVerificationCommandRequest("Late", @"C:\dotnet.exe", ["test"], 300, true));

        Assert.Equal(HttpStatusCode.Conflict, review.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, bundle.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, recipe.StatusCode);
        await using var after = OpenDb();
        Assert.Equal(reviewsBefore, await after.CheckpointReviews.AsNoTracking().CountAsync());
        Assert.Equal(membersBefore, await after.CheckpointReviewEvidence.AsNoTracking().CountAsync());
    }
}
