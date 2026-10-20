using System.Net;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.ExecuteLocalCommit;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0058 beside ADR-0029's local commit, through the production host and real Git: a local commit is one of at most two recorded
/// potentially mutating owners across the database. Admission is refused (before preparation and again at the locked reservation seam)
/// with a fixed Conflict code and nothing reserved, persisted or run; its single-use execution gate ignores only its own operation and
/// refuses beside two other owners without consuming the marker; the approval identity is unchanged.
/// </summary>
public sealed class LocalCommitGlobalAdmissionTests : LocalCommitTestBase
{
    private const string LimitReached = "execution_capacity.limit_reached";
    private const string Unavailable = "execution_capacity.unavailable";
    private static readonly string Fingerprint = new('a', 64);

    /// <summary>One unrelated project with its own Ready workspace and one Running verification execution, as production rows look.</summary>
    private static Task<Guid> PlantVerificationOwnerAsync(LocalCommitHost host) => WithDbAsync(host, async db =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-30);
        var project = Project.Register(Guid.NewGuid(), "Capacity neighbour", $@"C:\repos\{Guid.NewGuid():N}", now);
        var recipe = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Unit", @"C:\dotnet.exe", ["test"], 60, true, now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\neighbour-{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []);
        db.Projects.Add(project);
        db.VerificationCommands.Add(recipe);
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(checkpoint);
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        await db.SaveChangesAsync();
        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, 1, workspace, checkpoint, recipe, now.AddMinutes(5));
        db.VerificationExecutions.Add(execution);
        await db.SaveChangesAsync();
        return execution.Id;
    });

    private static Task FinishVerificationOwnerAsync(LocalCommitHost host, Guid executionId) => WithDbAsync(host, async db =>
    {
        await db.VerificationExecutions.Where(execution => execution.Id == executionId)
            .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.Status, VerificationExecutionStatus.Passed));
        return 0;
    });

    private async Task AssertNothingAdmittedAsync(LocalCommitHost host, LocalCommitLineageIds ids)
    {
        Assert.Equal(0, await OperationCountAsync(host));
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(RunLifecycle.Running, (await RunRowAsync(ids.RunId)).Lifecycle);
        Assert.DoesNotContain(
            await EventTypeRowsAsync(ids.RunId), type => type.StartsWith("local_commit.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_database_holding_two_other_owners_refuses_admission_before_any_preparation_and_persists_nothing()
    {
        var preparer = new ScriptedLocalCommitPreparer();
        var (host, script) = StartScripted(supervisor: false, decoratePreparer: inner => preparer.Attach(inner));
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        await PlantVerificationOwnerAsync(host);
        await PlantVerificationOwnerAsync(host);

        var response = await PostAsync(host, ids.RunId, ids);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(LimitReached, await ErrorCodeAsync(response));
        Assert.Equal(0, preparer.Entered);
        Assert.Empty(script.Calls);
        await AssertNothingAdmittedAsync(host, ids);
    }

    [Fact]
    public async Task One_other_owner_leaves_the_last_place_and_the_approved_commit_is_admitted_and_reserved()
    {
        var (host, _) = StartScripted(supervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        await PlantVerificationOwnerAsync(host);

        var response = await PostAsync(host, ids.RunId, ids);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(WorkspaceStatus.Committing, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
        Assert.Equal(LocalCommitStatus.Prepared, (await OperationRowAsync(ids.RunId)).Status);
    }

    [Fact]
    public async Task An_owner_that_takes_the_last_place_after_preparation_refuses_the_locked_admission_and_reserves_nothing()
    {
        var preparer = new ScriptedLocalCommitPreparer();
        LocalCommitHost? hostForCallback = null;
        preparer.AfterPrepare = async completion =>
        {
            Assert.True(completion.IsPrepared);
            await PlantVerificationOwnerAsync(hostForCallback!);
        };
        var (host, script) = StartScripted(supervisor: false, decoratePreparer: inner => preparer.Attach(inner));
        hostForCallback = host;
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        await PlantVerificationOwnerAsync(host);

        var response = await PostAsync(host, ids.RunId, ids);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(LimitReached, await ErrorCodeAsync(response));
        Assert.Equal(1, preparer.Entered);
        Assert.Empty(script.Calls);
        await AssertNothingAdmittedAsync(host, ids);
    }

    [Fact]
    public async Task Unattestable_recorded_ownership_anywhere_refuses_admission_as_unavailable_and_persists_nothing()
    {
        var (host, script) = StartScripted(supervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        var execution = await PlantVerificationOwnerAsync(host);
        await ExecuteSqlAsync("PRAGMA foreign_keys = OFF;");
        await using (var db = OpenDb())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE verification_executions SET GitWorkspaceId = {Guid.NewGuid()} WHERE Id = {execution}");
        }

        var response = await PostAsync(host, ids.RunId, ids);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(Unavailable, await ErrorCodeAsync(response));
        Assert.Empty(script.Calls);
        await AssertNothingAdmittedAsync(host, ids);
    }

    [Fact]
    public async Task Execution_ignores_only_its_own_reservation_refuses_beside_two_other_owners_and_runs_once_a_place_is_freed()
    {
        var (host, script) = StartScripted(supervisor: false);
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids)).StatusCode);
        var operationId = (await OperationRowAsync(ids.RunId)).Id;
        var first = await PlantVerificationOwnerAsync(host);
        await PlantVerificationOwnerAsync(host);

        // Three recorded owners: the admitted commit and two others. Excluding exactly itself leaves two, so it cannot execute.
        var refused = await ExecuteAsync(host, operationId);

        Assert.True(refused.IsFailure);
        Assert.Equal(LimitReached, Assert.Single(refused.Errors).Code);
        Assert.Empty(script.Calls);
        var operation = await OperationRowAsync(ids.RunId);
        Assert.Equal(LocalCommitStatus.Prepared, operation.Status);
        Assert.Equal(WorkspaceStatus.Committing, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);

        // A genuine terminal transition frees one place: the commit now executes beside the one remaining owner.
        await FinishVerificationOwnerAsync(host, first);
        var executed = await ExecuteAsync(host, operationId);

        Assert.True(executed.IsSuccess, executed.IsFailure ? executed.Errors[0].Code : string.Empty);
        Assert.Equal(LocalCommitStatus.Completed, (await OperationRowAsync(ids.RunId)).Status);
        Assert.Equal(WorkspaceStatus.Ready, (await WorkspaceRowAsync(ids.WorkspaceId)).Status);
    }

    private static async Task<Devalente.Shared.Results.Result<LocalCommitOperationView>> ExecuteAsync(LocalCommitHost host, Guid operationId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        return await mediator.SendAsync(new ExecuteLocalCommitCommand(operationId), CancellationToken.None);
    }
}
