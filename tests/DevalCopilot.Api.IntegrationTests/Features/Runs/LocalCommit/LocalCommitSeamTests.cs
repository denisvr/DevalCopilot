using System.Net;
using System.Net.Http.Json;
using DevalCopilot.Api.Features.Projects.ConfigureVerificationCommand;
using DevalCopilot.Api.Features.Projects.RecordCheckpointReview;
using DevalCopilot.Api.Features.Projects.UpdateVerificationCommand;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// The Committing reservation and the competing writers, in BOTH race directions, through the production host.
/// Reservation first: every competing write is refused at its own seam and persists nothing. Competitor first: a write that
/// commits after the pre-admission reads and before the locked admission transaction is seen by the locked re-read, the whole
/// admission rolls back (including the reservation) and the competitor's write is the only change.
/// </summary>
public sealed class LocalCommitSeamTests : LocalCommitTestBase
{
    private sealed record Counts(int Checkpoints, int Reviews, int Recipes, int Executions, int Attempts);

    private async Task<Counts> CountsAsync(Guid projectId)
    {
        await using var db = OpenDb();
        var workspaceIds = await db.GitWorkspaces.AsNoTracking().Where(w => w.ProjectId == projectId).Select(w => w.Id).ToListAsync();
        return new Counts(
            await db.GitCheckpoints.AsNoTracking().CountAsync(c => workspaceIds.Contains(c.WorkspaceId)),
            await db.CheckpointReviews.AsNoTracking().CountAsync(r => r.ProjectId == projectId),
            await db.VerificationCommands.AsNoTracking().CountAsync(c => c.ProjectId == projectId),
            await db.VerificationExecutions.AsNoTracking().CountAsync(e => e.ProjectId == projectId),
            await db.Attempts.AsNoTracking().CountAsync());
    }

    /// <summary>Every competing write the reservation excludes, each attempted once through the production host.</summary>
    private static async Task<Dictionary<string, HttpStatusCode>> CompetingWritesAsync(LocalCommitHost host, LocalCommitLineageIds ids)
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        return new Dictionary<string, HttpStatusCode>
        {
            ["capture"] = (await client.PostAsync($"/api/projects/{ids.ProjectId}/workspace/checkpoints", null)).StatusCode,
            ["configure"] = (await client.PostAsJsonAsync(
                $"/api/projects/{ids.ProjectId}/verification-commands",
                new ConfigureVerificationCommandRequest("Late", @"C:\dotnet.exe", ["test"], 300, true))).StatusCode,
            ["update"] = (await client.PutAsJsonAsync(
                $"/api/projects/{ids.ProjectId}/verification-commands/{ids.CommandId}",
                new UpdateVerificationCommandRequest("Changed", @"C:\dotnet.exe", ["test"], 300, true))).StatusCode,
            ["delete"] = (await client.DeleteAsync(
                $"/api/projects/{ids.ProjectId}/verification-commands/{ids.CommandId}")).StatusCode,
            ["review"] = (await client.PostAsJsonAsync(
                $"/api/projects/{ids.ProjectId}/reviews",
                new RecordCheckpointReviewRequest(ids.CheckpointId, ids.ExecutionId, "Human", "Approved"))).StatusCode,
            ["claim-verification"] = (await client.PostAsJsonAsync(
                $"/api/projects/{ids.ProjectId}/verification-commands/{ids.CommandId}/executions",
                new { gitCheckpointId = ids.CheckpointId })).StatusCode,
            ["agent-claim"] = (await client.PostAsync($"/api/runs/{ids.RunId}/agent-attempts/codex-plan", null)).StatusCode,
        };
    }

    private static void AssertEveryWriteRefused(Dictionary<string, HttpStatusCode> refusals)
    {
        foreach (var (name, status) in refusals)
        {
            Assert.True(status is HttpStatusCode.Conflict or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
                $"{name} was not refused: {status}");
        }
    }

    [Fact]
    public async Task While_the_workspace_is_reserved_every_competing_write_is_refused_and_persists_nothing()
    {
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);
        Assert.Equal(WorkspaceStatus.Committing, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        var before = await CountsAsync(ids.ProjectId);
        var refusals = await CompetingWritesAsync(host, ids);

        AssertEveryWriteRefused(refusals);

        Assert.Equal(before, await CountsAsync(ids.ProjectId));
        Assert.Equal(WorkspaceStatus.Committing, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(LocalCommitStatus.Prepared, (await OperationRowAsync(ids.RunId)).Status);
    }

    [Fact]
    public async Task While_an_ambiguous_operation_requires_attention_every_competing_write_is_still_refused_and_persists_nothing()
    {
        var (host, script) = StartScripted(supervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        // The production foreign-index-lock refusal: another process holds index.lock, so the outcome cannot be proven.
        script.BeforeAcquire = _ =>
        {
            File.WriteAllText(Scene.IndexPath + ".lock", "another process");
            return Task.CompletedTask;
        };
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);
        await WaitUntilAsync(async () => (await OperationRowAsync(ids.RunId)).Status == LocalCommitStatus.NeedsAttention);
        Assert.Equal(WorkspaceStatus.NeedsAttention, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
        var before = await CountsAsync(ids.ProjectId);

        var refusals = await CompetingWritesAsync(host, ids);

        AssertEveryWriteRefused(refusals);
        Assert.Equal(HttpStatusCode.Conflict, refusals["configure"]);
        Assert.Equal(HttpStatusCode.Conflict, refusals["update"]);
        Assert.Equal(HttpStatusCode.Conflict, refusals["delete"]);
        Assert.Equal(before, await CountsAsync(ids.ProjectId));
        Assert.Equal(WorkspaceStatus.NeedsAttention, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(LocalCommitStatus.NeedsAttention, (await OperationRowAsync(ids.RunId)).Status);
        Assert.Equal("another process", File.ReadAllText(Scene.IndexPath + ".lock"));
    }

    [Fact]
    public async Task A_recipe_request_is_accepted_again_once_a_proven_release_ended_the_reservation()
    {
        var (host, script) = StartScripted(supervisor: true);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        script.BeforeAcquire = facts =>
        {
            // Only the host-owned prepared artifact is damaged, so the refusal is proven unpromoted and the reservation is released.
            File.AppendAllText(Scene.Storage.ResolveArtifact(facts.PreparedIndexRelativePath)!, "tamper");
            return Task.CompletedTask;
        };
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);
        await WaitUntilAsync(async () => (await OperationRowAsync(ids.RunId)).Status == LocalCommitStatus.Failed);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        var before = await CountsAsync(ids.ProjectId);
        using var client = LocalCommitLineage.AuthenticatedClient(host);

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{ids.ProjectId}/verification-commands",
            new ConfigureVerificationCommandRequest("After release", @"C:\dotnet.exe", ["test"], 300, true));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(before.Recipes + 1, (await CountsAsync(ids.ProjectId)).Recipes);
    }

    public enum Competitor
    {
        NewCheckpoint,
        NewEnabledRecipe,
        RecipeUpdated,
        HumanChangesRequested,
        AnotherApprovedHuman,
    }

    [Theory]
    [InlineData(Competitor.NewCheckpoint, "local_commit.checkpoint_not_current")]
    [InlineData(Competitor.NewEnabledRecipe, "local_commit.verification_not_current")]
    [InlineData(Competitor.RecipeUpdated, "local_commit.verification_not_current")]
    [InlineData(Competitor.HumanChangesRequested, "local_commit.human_decision_not_approved")]
    [InlineData(Competitor.AnotherApprovedHuman, "local_commit.authority_changed")]
    public async Task A_competitor_that_commits_after_the_pre_reads_is_seen_by_the_locked_admission_and_everything_rolls_back(
        Competitor competitor, string expectedCode)
    {
        var preparer = new ScriptedLocalCommitPreparer();
        using var host = StartHost(runSupervisor: false, decoratePreparer: inner => preparer.Attach(inner));
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var preparedInWindow = false;
        preparer.AfterPrepare = async _ =>
        {
            preparedInWindow = true;
            using var client = LocalCommitLineage.AuthenticatedClient(host);
            HttpResponseMessage response = competitor switch
            {
                Competitor.NewCheckpoint => await client.PostAsync($"/api/projects/{ids.ProjectId}/workspace/checkpoints", null),
                Competitor.NewEnabledRecipe => await client.PostAsJsonAsync(
                    $"/api/projects/{ids.ProjectId}/verification-commands",
                    new ConfigureVerificationCommandRequest("Racing", @"C:\dotnet.exe", ["test"], 300, true)),
                Competitor.RecipeUpdated => await client.PutAsJsonAsync(
                    $"/api/projects/{ids.ProjectId}/verification-commands/{ids.CommandId}",
                    new UpdateVerificationCommandRequest("Racing", @"C:\dotnet.exe", ["test"], 300, true)),
                _ => await client.PostAsJsonAsync(
                    $"/api/projects/{ids.ProjectId}/reviews",
                    new RecordCheckpointReviewRequest(ids.CheckpointId, ids.ExecutionId, "Human", competitor == Competitor.AnotherApprovedHuman ? "Approved" : "ChangesRequested")),
            };
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        };
        var before = await CountsAsync(ids.ProjectId);

        var response = await PostAsync(host, ids.RunId, ids);

        Assert.True(preparedInWindow);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(expectedCode, await ErrorCodeAsync(response));
        Assert.Equal(0, await OperationCountAsync(host));
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.DoesNotContain(
            await EventTypeRowsAsync(ids.RunId), type => type.StartsWith("local_commit.", StringComparison.Ordinal));
        var after = await CountsAsync(ids.ProjectId);
        Assert.Equal(before.Attempts, after.Attempts);
        Assert.Equal(competitor == Competitor.NewCheckpoint ? before.Checkpoints + 1 : before.Checkpoints, after.Checkpoints);
        Assert.Equal(competitor == Competitor.NewEnabledRecipe ? before.Recipes + 1 : before.Recipes, after.Recipes);
        Assert.Equal(competitor is Competitor.HumanChangesRequested or Competitor.AnotherApprovedHuman ? before.Reviews + 1 : before.Reviews, after.Reviews);
    }

    [Fact]
    public async Task Concurrent_admission_and_competing_writes_never_leave_an_operation_with_changed_authority()
    {
        // The window cannot be hit deterministically from outside, so many real concurrent attempts assert the invariant the
        // reservation exists for: whenever an operation was admitted, no competing checkpoint or recipe landed after it.
        using var host = StartHost(runSupervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        using var client = LocalCommitLineage.AuthenticatedClient(host);

        var results = await Task.WhenAll(
            PostAsync(host, ids.RunId, ids),
            client.PostAsync($"/api/projects/{ids.ProjectId}/workspace/checkpoints", null),
            client.PostAsJsonAsync(
                $"/api/projects/{ids.ProjectId}/verification-commands",
                new ConfigureVerificationCommandRequest("Racing", @"C:\dotnet.exe", ["test"], 300, true)),
            client.PostAsJsonAsync(
                $"/api/projects/{ids.ProjectId}/reviews",
                new RecordCheckpointReviewRequest(ids.CheckpointId, ids.ExecutionId, "Human", "ChangesRequested")));

        Assert.All(results, response => Assert.True(
            (int)response.StatusCode is >= 200 and < 300 || response.StatusCode == HttpStatusCode.Conflict,
            $"Unexpected {response.StatusCode}"));
        await using var db = OpenDb();
        var operation = await db.LocalCommitOperations.AsNoTracking().SingleOrDefaultAsync();
        if (operation is not null)
        {
            var latest = await db.GitCheckpoints.AsNoTracking().Where(c => c.WorkspaceId == ids.WorkspaceId)
                .OrderByDescending(c => c.CheckpointNumber).FirstAsync();
            Assert.Equal(operation.GitCheckpointId, latest.Id);
            Assert.Equal(1, await db.VerificationCommands.AsNoTracking().CountAsync(c => c.ProjectId == ids.ProjectId));
            Assert.Equal(
                1, await db.CheckpointReviews.AsNoTracking().CountAsync(r => r.GitCheckpointId == ids.CheckpointId && r.ActorKind == ReviewActorKind.Human));
        }
        else
        {
            Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        }
    }
}
