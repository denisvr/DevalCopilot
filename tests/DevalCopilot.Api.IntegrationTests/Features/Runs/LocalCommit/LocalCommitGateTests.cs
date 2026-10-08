using System.Net;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Projects.CaptureGitWorkspaceCheckpoint;
using DevalCopilot.Api.Features.Projects.ConfigureVerificationCommand;
using DevalCopilot.Api.Features.Projects.UpdateVerificationCommand;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0029's approval and exclusion gates through the protected API with the lineage written by the production handlers.
/// Every refusal has its own stable code; the advisory status and the command agree; nothing is written, reserved or run; and the
/// untouched positive control still commits. The authority is broken only by one targeted change per case.
/// </summary>
public sealed class LocalCommitGateTests : LocalCommitTestBase
{
    private static string Upper(Guid id) => id.ToString().ToUpperInvariant();

    private async Task AssertRefusedAsync(LocalCommitHost host, LocalCommitLineageIds ids, string code)
    {
        var status = await StatusAsync(host, ids.RunId);
        Assert.False(status.Eligible);
        Assert.Equal(code, status.RefusalCode);

        var response = await PostAsync(host, ids.RunId, ids);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, await ErrorCodeAsync(response));
        Assert.Equal(0, await OperationCountAsync(host));
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.DoesNotContain(
            await EventTypeRowsAsync(ids.RunId), type => type.StartsWith("local_commit.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_unmodified_lineage_is_eligible_and_commits_control()
    {
        using var host = StartHost(runSupervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var status = await StatusAsync(host, ids.RunId);
        Assert.True(status.Eligible, status.RefusalCode);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);

        await WaitForAsync(host, ids.RunId, current => current.Operation?.Status == "Completed");
    }

    [Fact]
    public async Task Two_enabled_recipes_cannot_be_covered_by_the_legacy_single_member_human_approval_and_refuse()
    {
        // The legacy scalar form records exactly one selected execution, while the Agent review claimed both enabled recipes, so
        // the memberships are not equal. The refusal stays the conservative contract, never a silent partial approval; the complete
        // Human approval of ADR-0030 (see LocalCommitCompleteSetTests) is the way to cover several recipes.
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene, recipes: 2);

        await AssertRefusedAsync(host, ids, "local_commit.membership_mismatch");
    }

    [Fact]
    public async Task A_missing_human_approval_refuses()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene, humanApproval: false);

        var status = await StatusAsync(host, ids.RunId);
        Assert.False(status.Eligible);
        Assert.Equal("local_commit.human_approval_missing", status.RefusalCode);
        var response = await PostAsync(host, ids.RunId, ids, humanReviewId: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("local_commit.human_approval_missing", await ErrorCodeAsync(response));
        Assert.Equal(0, await OperationCountAsync(host));
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("ChangesRequested")]
    [InlineData("Escalated")]
    public async Task Any_additional_non_approved_human_decision_on_the_checkpoint_blocks_and_never_infers_supersession(string decision)
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        // A Pending decision cannot carry evidence; every other decision records the same selected execution.
        await LocalCommitLineage.RecordHumanApprovalAsync(
            host, ids.ProjectId, ids.CheckpointId, decision == "Pending" ? null : ids.ExecutionId, decision);

        await AssertRefusedAsync(host, ids, "local_commit.human_decision_not_approved");
    }

    [Fact]
    public async Task A_newer_checkpoint_makes_the_selected_one_not_current_and_the_new_one_unapproved()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        using (var client = LocalCommitLineage.AuthenticatedClient(host))
        {
            var captured = await client.PostAsync($"/api/projects/{ids.ProjectId}/workspace/checkpoints", null);
            Assert.Equal(HttpStatusCode.OK, captured.StatusCode);
            Assert.NotNull(await captured.Content.ReadFromJsonAsync<CaptureGitWorkspaceCheckpointResponse>());
        }

        var pinned = await PostAsync(host, ids.RunId, ids);
        Assert.Equal(HttpStatusCode.Conflict, pinned.StatusCode);
        Assert.Equal("local_commit.checkpoint_not_current", await ErrorCodeAsync(pinned));
        var status = await StatusAsync(host, ids.RunId);
        Assert.False(status.Eligible);
        Assert.Equal("local_commit.agent_approval_missing", status.RefusalCode);
        Assert.Equal(0, await OperationCountAsync(host));
    }

    [Fact]
    public async Task A_recipe_enabled_after_the_verification_has_no_current_execution()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        using (var client = LocalCommitLineage.AuthenticatedClient(host))
        {
            var response = await client.PostAsJsonAsync(
                $"/api/projects/{ids.ProjectId}/verification-commands",
                new ConfigureVerificationCommandRequest("Late recipe", @"C:\dotnet.exe", ["test"], 300, true));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        await AssertRefusedAsync(host, ids, "local_commit.verification_not_current");
    }

    [Fact]
    public async Task A_recipe_edited_after_its_execution_no_longer_matches_the_exact_command_snapshot()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        using (var client = LocalCommitLineage.AuthenticatedClient(host))
        {
            var response = await client.PutAsJsonAsync(
                $"/api/projects/{ids.ProjectId}/verification-commands/{ids.CommandId}",
                new UpdateVerificationCommandRequest("Edited recipe", @"C:\dotnet.exe", ["test"], 300, true));
            Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString());
        }

        await AssertRefusedAsync(host, ids, "local_commit.verification_not_current");
    }

    [Fact]
    public async Task A_recipe_disabled_after_review_changes_the_membership_the_reviews_claimed()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene, recipes: 2);
        await using (var db = OpenDb())
        {
            var second = await db.VerificationCommands.AsNoTracking()
                .Where(command => command.ProjectId == ids.ProjectId && command.Id != ids.CommandId)
                .SingleAsync();
            using var client = LocalCommitLineage.AuthenticatedClient(host);
            var response = await client.PutAsJsonAsync(
                $"/api/projects/{ids.ProjectId}/verification-commands/{second.Id}",
                new UpdateVerificationCommandRequest(second.Name, second.ExecutablePath, second.Arguments, second.TimeoutSeconds, false));
            Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString());
        }

        await AssertRefusedAsync(host, ids, "local_commit.membership_mismatch");
    }

    [Theory]
    [InlineData("UPDATE verification_executions SET \"Status\" = 'Failed' WHERE \"Id\" = '{execution}'", "local_commit.verification_not_current")]
    [InlineData("UPDATE verification_executions SET \"CompletionFingerprintSha256\" = '{fingerprint}' WHERE \"Id\" = '{execution}'", "local_commit.verification_not_current")]
    [InlineData("UPDATE verification_executions SET \"ExitCode\" = 1 WHERE \"Id\" = '{execution}'", "local_commit.verification_not_current")]
    [InlineData("UPDATE verification_executions SET \"CommandName\" = 'tampered' WHERE \"Id\" = '{execution}'", "local_commit.verification_not_current")]
    [InlineData("UPDATE verification_executions SET \"Status\" = 'Running' WHERE \"Id\" = '{execution}'", "local_commit.active_work")]
    [InlineData("UPDATE attempts SET \"Status\" = 'Running' WHERE \"Id\" = '{review}'", "local_commit.active_work")]
    [InlineData("UPDATE attempts SET \"AgentOutcome\" = 'ReviewChangesRequested' WHERE \"Id\" = '{review}'", "local_commit.agent_approval_missing")]
    [InlineData("UPDATE collaboration_messages SET \"Provenance\" = 'HostConstructed' WHERE \"Type\" = 'ReviewApproval'", "local_commit.agent_approval_missing")]
    [InlineData("UPDATE collaboration_messages SET \"Type\" = 'ReviewFinding' WHERE \"Type\" = 'ReviewApproval'", "local_commit.agent_approval_missing")]
    [InlineData("DELETE FROM attempt_input_messages WHERE \"AttemptId\" = '{review}'", "local_commit.implementation_not_current")]
    [InlineData("UPDATE runs SET \"Lifecycle\" = 'Completed' WHERE \"Id\" = '{run}'", "local_commit.run_not_eligible")]
    [InlineData("UPDATE repository_mutation_leases SET \"Status\" = 'Released' WHERE \"Id\" = '{lease}'", "local_commit.lease_not_active")]
    [InlineData("UPDATE git_workspaces SET \"Status\" = 'NeedsAttention' WHERE \"Id\" = '{workspace}'", "local_commit.workspace_not_ready")]
    [InlineData("UPDATE git_workspaces SET \"Status\" = 'Committing' WHERE \"Id\" = '{workspace}'", "local_commit.workspace_not_ready")]
    [InlineData("UPDATE git_checkpoints SET \"HeadCommitSha\" = '{fingerprint40}' WHERE \"Id\" = '{checkpoint}'", "local_commit.parent_mismatch")]
    [InlineData("UPDATE git_workspaces SET \"SourceCommitSha\" = '{fingerprint40}' WHERE \"Id\" = '{workspace}'", "local_commit.parent_mismatch")]
    public async Task A_targeted_authority_change_refuses_with_its_own_stable_code(string sql, string code)
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        await ExecuteSqlAsync(sql
            .Replace("{execution}", Upper(ids.ExecutionId), StringComparison.Ordinal)
            .Replace("{review}", Upper(ids.ReviewAttemptId), StringComparison.Ordinal)
            .Replace("{run}", Upper(ids.RunId), StringComparison.Ordinal)
            .Replace("{lease}", Upper(ids.LeaseId), StringComparison.Ordinal)
            .Replace("{workspace}", Upper(ids.WorkspaceId), StringComparison.Ordinal)
            .Replace("{checkpoint}", Upper(ids.CheckpointId), StringComparison.Ordinal)
            .Replace("{fingerprint}", new string('f', 64), StringComparison.Ordinal)
            .Replace("{fingerprint40}", new string('f', 40), StringComparison.Ordinal));

        var response = await PostAsync(host, ids.RunId, ids);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, await ErrorCodeAsync(response));
        Assert.Equal(0, await OperationCountAsync(host));
        var workspaceStatus = (await WorkspaceRowAsync(ids.WorkspaceId)).Status;
        Assert.True(workspaceStatus is WorkspaceStatus.Ready or WorkspaceStatus.NeedsAttention or WorkspaceStatus.Committing);
        Assert.DoesNotContain(
            await EventTypeRowsAsync(ids.RunId), type => type.StartsWith("local_commit.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_status_is_advisory_the_command_decides_again_from_fresh_authority()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var eligible = await StatusAsync(host, ids.RunId);
        Assert.True(eligible.Eligible);

        await LocalCommitLineage.RecordHumanApprovalAsync(host, ids.ProjectId, ids.CheckpointId, ids.ExecutionId, "ChangesRequested");

        var response = await PostAsync(host, ids.RunId, ids);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("local_commit.human_decision_not_approved", await ErrorCodeAsync(response));
        Assert.Equal(0, await OperationCountAsync(host));
    }
}
