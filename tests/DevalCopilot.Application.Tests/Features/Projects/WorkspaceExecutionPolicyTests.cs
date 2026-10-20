using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Tests.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// ADR-0057's single reading of who may execute in a project's owned workspace, over a real file-backed SQLite database. Ownership is
/// derived only from existing rows; a terminal owner never blocks, an unrecognized stored state is never finished, conflicting or
/// untrustworthy ownership is never resolved by choosing a winner, and distinct projects never interact.
/// </summary>
public sealed class WorkspaceExecutionPolicyTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<WorkspaceExecutionAssessment> AssessAsync(
        WorkspaceExecutionScene scene, Guid? excludedAttemptId = null, Guid? excludedExecutionId = null)
    {
        await using var db = fixture.CreateContext();
        return await WorkspaceExecutionPolicy.AssessAsync(
            db, scene.Project.Id, scene.Workspace.Id, excludedAttemptId, excludedExecutionId, CancellationToken.None);
    }

    private async Task<WorkspaceExecutionScene> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        return await WorkspaceExecutionSeed.SeedAsync(db);
    }

    // ---- The closed readings --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_project_without_a_workspace_reads_NoWorkspace_with_no_workspace_identity()
    {
        await using var db = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Empty", $@"C:\repos\{Guid.NewGuid():N}", WorkspaceExecutionSeed.Now);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var assessment = await WorkspaceExecutionPolicy.AssessLatestAsync(db, project.Id, CancellationToken.None);

        Assert.Equal(new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.NoWorkspace, null), assessment);
    }

    [Fact]
    public async Task A_ready_leased_workspace_without_an_owner_reads_Idle_for_exactly_the_latest_workspace()
    {
        var scene = await SeedAsync();

        await using var db = fixture.CreateContext();
        var assessment = await WorkspaceExecutionPolicy.AssessLatestAsync(db, scene.Project.Id, CancellationToken.None);

        Assert.Equal(new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.Idle, scene.Workspace.Id), assessment);
    }

    [Fact]
    public async Task The_latest_workspace_by_number_is_the_one_judged_never_an_older_one()
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            var newer = GitWorkspace.Prepare(
                Guid.NewGuid(), scene.Project.Id, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "branch-2", new string('a', 40), "main",
                WorkspaceExecutionSeed.Now);
            newer.MarkReady();
            db.GitWorkspaces.Add(newer);
            db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
                Guid.NewGuid(), scene.Project.Id, newer.Id, 2, Guid.NewGuid().ToByteArray(), WorkspaceExecutionSeed.Now));
            await db.SaveChangesAsync();
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);

            var assessment = await WorkspaceExecutionPolicy.AssessLatestAsync(db, scene.Project.Id, CancellationToken.None);

            Assert.Equal(new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.Idle, newer.Id), assessment);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_running_Agent_attempt_of_the_workspace_owns_it_from_its_claim_dispatched_or_not(bool dispatched)
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id, dispatched);
        }

        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, (await AssessAsync(scene)).Status);
    }

    [Fact]
    public async Task A_running_Agent_attempt_of_a_different_run_of_the_same_project_owns_the_same_workspace()
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            var other = await WorkspaceExecutionSeed.AddRunAsync(db, scene.Project.Id, 2);
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, other.Id, scene.Workspace.Id, scene.Checkpoint.Id);
        }

        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, (await AssessAsync(scene)).Status);
    }

    [Fact]
    public async Task A_running_verification_owns_the_workspace_regardless_of_its_recipe_or_dispatch_marker()
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene, dispatched: false);
            await db.VerificationCommands.Where(recipe => recipe.Id == scene.Recipe.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(recipe => recipe.IsEnabled, false));
        }

        Assert.Equal(WorkspaceExecutionStatus.VerificationActive, (await AssessAsync(scene)).Status);
    }

    [Theory]
    [InlineData(WorkspaceStatus.Committing, LocalCommitStatus.Prepared)]
    [InlineData(WorkspaceStatus.Committing, LocalCommitStatus.Executing)]
    [InlineData(WorkspaceStatus.NeedsAttention, LocalCommitStatus.NeedsAttention)]
    [InlineData(WorkspaceStatus.NeedsAttention, LocalCommitStatus.Executing)]
    public async Task ADR_0029_s_reservation_is_an_owner_while_its_operation_is_nonterminal(WorkspaceStatus workspace, LocalCommitStatus operation)
    {
        await using var db = fixture.CreateContext();
        var rows = await WorkspaceExecutionSeed.SeedLocalCommitAsync(db, operation, workspace);

        var assessment = await WorkspaceExecutionPolicy.AssessAsync(
            db, rows.Project.Id, rows.Workspace.Id, null, null, CancellationToken.None);

        Assert.Equal(new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.CommitReserved, rows.Workspace.Id), assessment);
    }

    [Fact]
    public async Task A_Committing_workspace_is_reserved_even_before_its_operation_row_exists()
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            await WorkspaceExecutionSeed.SetWorkspaceStatusAsync(db, scene.Workspace.Id, WorkspaceStatus.Committing);
        }

        Assert.Equal(WorkspaceExecutionStatus.CommitReserved, (await AssessAsync(scene)).Status);
    }

    // ---- Conflicts: no winner is ever chosen ----------------------------------------------------------------------------

    [Fact]
    public async Task An_Agent_attempt_beside_a_verification_execution_is_Conflicted()
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
            await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene);
        }

        Assert.Equal(WorkspaceExecutionStatus.Conflicted, (await AssessAsync(scene)).Status);
    }

    [Fact]
    public async Task Two_Agent_attempts_of_different_runs_are_Conflicted_not_one_of_them_chosen()
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            var other = await WorkspaceExecutionSeed.AddRunAsync(db, scene.Project.Id, 2);
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, other.Id, scene.Workspace.Id, scene.Checkpoint.Id);
        }

        Assert.Equal(WorkspaceExecutionStatus.Conflicted, (await AssessAsync(scene)).Status);
    }

    [Fact]
    public async Task An_Agent_attempt_beside_a_commit_reservation_is_Conflicted()
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
            await WorkspaceExecutionSeed.SetWorkspaceStatusAsync(db, scene.Workspace.Id, WorkspaceStatus.Committing);
        }

        Assert.Equal(WorkspaceExecutionStatus.Conflicted, (await AssessAsync(scene)).Status);
    }

    [Fact]
    public async Task Conflicting_owners_stay_Conflicted_when_one_of_them_is_excluded_only_if_another_owner_remains()
    {
        var scene = await SeedAsync();
        Guid attemptId;
        await using (var db = fixture.CreateContext())
        {
            attemptId = await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
            await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene);
        }

        Assert.Equal(WorkspaceExecutionStatus.VerificationActive, (await AssessAsync(scene, excludedAttemptId: attemptId)).Status);
    }

    // ---- Terminal history never blocks, unrecognized states always do ----------------------------------------------------

    [Theory]
    [InlineData(AttemptStatus.Completed)]
    [InlineData(AttemptStatus.Failed)]
    [InlineData(AttemptStatus.Interrupted)]
    public async Task A_terminal_Agent_attempt_never_owns_the_workspace(AttemptStatus status)
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            var id = await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
            await WorkspaceExecutionSeed.SetAttemptStatusAsync(db, id, status);
        }

        Assert.Equal(WorkspaceExecutionStatus.Idle, (await AssessAsync(scene)).Status);
    }

    [Theory]
    [InlineData(VerificationExecutionStatus.Passed)]
    [InlineData(VerificationExecutionStatus.Failed)]
    [InlineData(VerificationExecutionStatus.TimedOut)]
    [InlineData(VerificationExecutionStatus.Cancelled)]
    [InlineData(VerificationExecutionStatus.Interrupted)]
    [InlineData(VerificationExecutionStatus.SourceChanged)]
    public async Task A_terminal_verification_execution_never_owns_the_workspace(VerificationExecutionStatus status)
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            var id = await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene);
            await WorkspaceExecutionSeed.SetExecutionStatusAsync(db, id, status);
        }

        Assert.Equal(WorkspaceExecutionStatus.Idle, (await AssessAsync(scene)).Status);
    }

    [Fact]
    public async Task A_terminal_local_commit_operation_does_not_reserve_a_Ready_workspace()
    {
        await using var db = fixture.CreateContext();
        var rows = await WorkspaceExecutionSeed.SeedLocalCommitAsync(db, LocalCommitStatus.Completed, WorkspaceStatus.Ready);

        var assessment = await WorkspaceExecutionPolicy.AssessAsync(
            db, rows.Project.Id, rows.Workspace.Id, null, null, CancellationToken.None);

        Assert.Equal(WorkspaceExecutionStatus.Idle, assessment.Status);
    }

    [Fact]
    public async Task An_unrecognized_Agent_attempt_status_is_potentially_active_never_finished()
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            var id = await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET Status = {"ZzUnrecognized"} WHERE Id = {id}");
        }

        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, (await AssessAsync(scene)).Status);
    }

    [Fact]
    public async Task An_unrecognized_verification_status_is_potentially_active_never_finished()
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            var id = await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE verification_executions SET Status = {"ZzUnrecognized"} WHERE Id = {id}");
        }

        Assert.Equal(WorkspaceExecutionStatus.VerificationActive, (await AssessAsync(scene)).Status);
    }

    // ---- Ownership that cannot be trusted is Unavailable ---------------------------------------------------------------

    [Fact]
    public async Task A_running_Agent_attempt_of_the_project_that_names_no_workspace_makes_the_reading_Unavailable()
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            var other = await WorkspaceExecutionSeed.AddRunAsync(db, scene.Project.Id, 2);
            var id = await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, other.Id, scene.Workspace.Id, scene.Checkpoint.Id);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentGitWorkspaceId = NULL WHERE Id = {id}");
        }

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await AssessAsync(scene)).Status);
    }

    [Fact]
    public async Task A_running_Agent_attempt_of_the_project_on_a_workspace_of_another_project_makes_the_reading_Unavailable()
    {
        var scene = await SeedAsync();
        var foreign = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            var other = await WorkspaceExecutionSeed.AddRunAsync(db, scene.Project.Id, 2);
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, other.Id, foreign.Workspace.Id, foreign.Checkpoint.Id);
        }

        // Both projects' readings fail closed: neither can trust an association that crosses the project boundary.
        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await AssessAsync(scene)).Status);
        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await AssessAsync(foreign)).Status);
    }

    [Fact]
    public async Task A_running_Agent_attempt_on_the_workspace_whose_run_belongs_to_another_project_makes_the_reading_Unavailable()
    {
        var scene = await SeedAsync();
        var foreign = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, foreign.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
        }

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await AssessAsync(scene)).Status);
    }

    [Fact]
    public async Task A_running_verification_on_the_workspace_that_names_another_project_makes_the_reading_Unavailable()
    {
        var scene = await SeedAsync();
        var foreign = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            var id = await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, foreign);
            await db.VerificationExecutions.Where(execution => execution.Id == id)
                .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.GitWorkspaceId, scene.Workspace.Id));
        }

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await AssessAsync(scene)).Status);
    }

    [Theory]
    [InlineData(WorkspaceStatus.Preparing)]
    [InlineData(WorkspaceStatus.FailedToPrepare)]
    [InlineData(WorkspaceStatus.MissingExternally)]
    [InlineData(WorkspaceStatus.AlteredExternally)]
    [InlineData(WorkspaceStatus.NeedsAttention)]
    public async Task A_workspace_that_is_not_Ready_and_not_reserved_is_Unavailable_never_Idle(WorkspaceStatus status)
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            await WorkspaceExecutionSeed.SetWorkspaceStatusAsync(db, scene.Workspace.Id, status);
        }

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await AssessAsync(scene)).Status);
    }

    [Fact]
    public async Task A_released_lease_is_Unavailable_never_Idle()
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            await db.RepositoryMutationLeases.Where(lease => lease.WorkspaceId == scene.Workspace.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released));
        }

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await AssessAsync(scene)).Status);
    }

    [Theory]
    [InlineData(LocalCommitStatus.Prepared)]
    [InlineData(LocalCommitStatus.Executing)]
    [InlineData(LocalCommitStatus.NeedsAttention)]
    public async Task A_nonterminal_operation_of_a_Ready_workspace_is_not_a_reservation_to_trust(LocalCommitStatus operation)
    {
        await using var db = fixture.CreateContext();
        var rows = await WorkspaceExecutionSeed.SeedLocalCommitAsync(db, operation, WorkspaceStatus.Ready);

        var assessment = await WorkspaceExecutionPolicy.AssessAsync(
            db, rows.Project.Id, rows.Workspace.Id, null, null, CancellationToken.None);

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, assessment.Status);
    }

    [Fact]
    public async Task A_NeedsAttention_workspace_without_an_open_operation_is_not_a_reservation()
    {
        await using var db = fixture.CreateContext();
        var rows = await WorkspaceExecutionSeed.SeedLocalCommitAsync(db, LocalCommitStatus.Completed, WorkspaceStatus.NeedsAttention);

        var assessment = await WorkspaceExecutionPolicy.AssessAsync(
            db, rows.Project.Id, rows.Workspace.Id, null, null, CancellationToken.None);

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, assessment.Status);
    }

    // ---- Scope: only the workspace's own owners, only this project's ----------------------------------------------------

    [Fact]
    public async Task A_historical_running_Agent_attempt_or_verification_of_an_older_workspace_does_not_block_the_current_one()
    {
        var scene = await SeedAsync();
        WorkspaceExecutionScene current;
        await using (var db = fixture.CreateContext())
        {
            var newer = GitWorkspace.Prepare(
                Guid.NewGuid(), scene.Project.Id, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "branch-2", new string('a', 40), "main",
                WorkspaceExecutionSeed.Now);
            newer.MarkReady();
            var checkpoint = GitCheckpoint.Capture(
                Guid.NewGuid(), newer.Id, 1, WorkspaceExecutionSeed.Now, new string('a', 40), WorkspaceExecutionSeed.Fingerprint, []);
            var lease = RepositoryMutationLease.Acquire(
                Guid.NewGuid(), scene.Project.Id, newer.Id, 2, Guid.NewGuid().ToByteArray(), WorkspaceExecutionSeed.Now);
            db.GitWorkspaces.Add(newer);
            db.GitCheckpoints.Add(checkpoint);
            db.RepositoryMutationLeases.Add(lease);
            await db.SaveChangesAsync();
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id);
            await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene);
            current = scene with { Workspace = newer, Checkpoint = checkpoint, Lease = lease };
        }

        Assert.Equal(WorkspaceExecutionStatus.Idle, (await AssessAsync(current)).Status);
        Assert.Equal(WorkspaceExecutionStatus.Conflicted, (await AssessAsync(scene)).Status);
    }

    [Fact]
    public async Task Distinct_projects_in_unrelated_repositories_never_affect_each_other()
    {
        var first = await SeedAsync();
        var second = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, first.Run.Id, first.Workspace.Id, first.Checkpoint.Id);
            await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, second);
        }

        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, (await AssessAsync(first)).Status);
        Assert.Equal(WorkspaceExecutionStatus.VerificationActive, (await AssessAsync(second)).Status);
    }

    [Fact]
    public async Task A_running_Process_or_Simulated_attempt_is_not_derived_into_an_owner_from_any_path_text()
    {
        var scene = await SeedAsync();
        await using (var db = fixture.CreateContext())
        {
            db.Attempts.Add(Attempt.Claim(Guid.NewGuid(), scene.Run.Id, 1, WorkspaceExecutionSeed.Now));
            await db.SaveChangesAsync();
        }

        Assert.Equal(WorkspaceExecutionStatus.Idle, (await AssessAsync(scene)).Status);
    }

    // ---- Exact self-exclusion --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Excluding_the_exact_Agent_attempt_leaves_the_workspace_Idle_for_its_own_dispatch()
    {
        var scene = await SeedAsync();
        Guid id;
        await using (var db = fixture.CreateContext())
        {
            id = await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id, dispatched: false);
        }

        Assert.Equal(WorkspaceExecutionStatus.Idle, (await AssessAsync(scene, excludedAttemptId: id)).Status);
        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, (await AssessAsync(scene, excludedAttemptId: Guid.NewGuid())).Status);
    }

    [Fact]
    public async Task Excluding_the_exact_verification_execution_leaves_the_workspace_Idle_for_its_own_dispatch()
    {
        var scene = await SeedAsync();
        Guid id;
        await using (var db = fixture.CreateContext())
        {
            id = await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene, dispatched: false);
        }

        Assert.Equal(WorkspaceExecutionStatus.Idle, (await AssessAsync(scene, excludedExecutionId: id)).Status);
        Assert.Equal(WorkspaceExecutionStatus.VerificationActive, (await AssessAsync(scene, excludedExecutionId: Guid.NewGuid())).Status);
    }

    [Fact]
    public async Task An_identifier_is_excluded_only_in_its_own_table()
    {
        var scene = await SeedAsync();
        var shared = Guid.NewGuid();
        await using (var db = fixture.CreateContext())
        {
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id, attemptId: shared);
            await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene, executionId: shared);
        }

        Assert.Equal(WorkspaceExecutionStatus.VerificationActive, (await AssessAsync(scene, excludedAttemptId: shared)).Status);
        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, (await AssessAsync(scene, excludedExecutionId: shared)).Status);
        Assert.Equal(WorkspaceExecutionStatus.Conflicted, (await AssessAsync(scene)).Status);
    }

    [Fact]
    public async Task Excluding_an_attempt_never_excludes_the_other_attempts_of_its_run()
    {
        var scene = await SeedAsync();
        Guid first;
        await using (var db = fixture.CreateContext())
        {
            first = await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id, attemptNumber: 1);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET Status = {"Interrupted"} WHERE Id = {first}");
            await WorkspaceExecutionSeed.AddAgentAttemptAsync(db, scene.Run.Id, scene.Workspace.Id, scene.Checkpoint.Id, attemptNumber: 2);
        }

        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, (await AssessAsync(scene, excludedAttemptId: first)).Status);
    }

    // ---- The reciprocal association checks (WEX-R1) ---------------------------------------------------------------------

    private async Task<WorkspaceExecutionAssessment> AssessProjectAsync(Guid projectId, Guid workspaceId)
    {
        await using var db = fixture.CreateContext();
        return await WorkspaceExecutionPolicy.AssessAsync(db, projectId, workspaceId, null, null, CancellationToken.None);
    }

    [Fact]
    public async Task An_active_verification_of_the_project_naming_a_workspace_of_another_project_makes_the_project_Unavailable()
    {
        WorkspaceExecutionScene own;
        await using (var seed = fixture.CreateContext())
        {
            own = await WorkspaceExecutionSeed.SeedAsync(seed);
            var foreign = await WorkspaceExecutionSeed.SeedAsync(seed);
            var executionId = await WorkspaceExecutionSeed.AddVerificationExecutionAsync(seed, own);
            await seed.VerificationExecutions.Where(candidate => candidate.Id == executionId)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.GitWorkspaceId, foreign.Workspace.Id));
        }

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await AssessAsync(own)).Status);
        await using var db = fixture.CreateContext();
        var latest = await WorkspaceExecutionPolicy.AssessLatestAsync(db, own.Project.Id, CancellationToken.None);
        Assert.Equal(new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.Unavailable, own.Workspace.Id), latest);
    }

    [Fact]
    public async Task An_open_commit_of_the_project_naming_a_workspace_of_another_project_makes_the_project_Unavailable()
    {
        Guid projectId;
        Guid workspaceId;
        await using (var seed = fixture.CreateContext())
        {
            var own = await WorkspaceExecutionSeed.SeedLocalCommitAsync(seed, LocalCommitStatus.Prepared, WorkspaceStatus.Ready);
            projectId = own.Project.Id;
            workspaceId = own.Workspace.Id;
            var foreign = await WorkspaceExecutionSeed.SeedAsync(seed);
            await seed.LocalCommitOperations.Where(candidate => candidate.Id == own.Operation.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.GitWorkspaceId, foreign.Workspace.Id));
        }

        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await AssessProjectAsync(projectId, workspaceId)).Status);
    }

    [Fact]
    public async Task A_foreign_association_never_blocks_the_other_project_or_a_finished_row()
    {
        WorkspaceExecutionScene own;
        WorkspaceExecutionScene foreign;
        Guid executionId;
        await using (var seed = fixture.CreateContext())
        {
            own = await WorkspaceExecutionSeed.SeedAsync(seed);
            foreign = await WorkspaceExecutionSeed.SeedAsync(seed);
            executionId = await WorkspaceExecutionSeed.AddVerificationExecutionAsync(seed, own);
            await seed.VerificationExecutions.Where(candidate => candidate.Id == executionId)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.GitWorkspaceId, foreign.Workspace.Id));
        }

        // The foreign project owns the workspace the malformed row names, so the row also makes THAT project's reading untrustworthy:
        // it is a stored association neither project can attest. An unrelated third project is unaffected.
        Assert.Equal(WorkspaceExecutionStatus.Unavailable, (await AssessAsync(foreign)).Status);
        var third = await SeedAsync();
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await AssessAsync(third)).Status);

        // Once the row is finished it is history: both projects read Idle again.
        await using (var db = fixture.CreateContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE verification_executions SET Status = {"Passed"} WHERE Id = {executionId}");
        }

        Assert.Equal(WorkspaceExecutionStatus.Idle, (await AssessAsync(own)).Status);
        Assert.Equal(WorkspaceExecutionStatus.Idle, (await AssessAsync(foreign)).Status);
    }

    [Fact]
    public async Task An_active_verification_of_an_older_workspace_of_the_same_project_does_not_make_the_latest_workspace_Unavailable()
    {
        var scene = await SeedAsync();
        await using var db = fixture.CreateContext();
        var newer = GitWorkspace.Prepare(
            Guid.NewGuid(), scene.Project.Id, 2, $@"C:\workspaces\{Guid.NewGuid():N}", "branch-2", new string('a', 40), "main",
            WorkspaceExecutionSeed.Now);
        newer.MarkReady();
        db.GitWorkspaces.Add(newer);
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), scene.Project.Id, newer.Id, 2, Guid.NewGuid().ToByteArray(), WorkspaceExecutionSeed.Now));
        await db.SaveChangesAsync();
        await WorkspaceExecutionSeed.AddVerificationExecutionAsync(db, scene);

        var latest = await WorkspaceExecutionPolicy.AssessLatestAsync(db, scene.Project.Id, CancellationToken.None);

        Assert.Equal(new WorkspaceExecutionAssessment(WorkspaceExecutionStatus.Idle, newer.Id), latest);
    }

    // ---- The stable refusals ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(WorkspaceExecutionStatus.Idle, null)]
    [InlineData(WorkspaceExecutionStatus.AgentAttemptActive, WorkspaceExecutionErrors.AgentAttemptActiveCode)]
    [InlineData(WorkspaceExecutionStatus.VerificationActive, WorkspaceExecutionErrors.VerificationActiveCode)]
    [InlineData(WorkspaceExecutionStatus.CommitReserved, CommitReservation.WorkspaceCommittingCode)]
    [InlineData(WorkspaceExecutionStatus.Conflicted, WorkspaceExecutionErrors.OwnersConflictedCode)]
    [InlineData(WorkspaceExecutionStatus.Unavailable, WorkspaceExecutionErrors.UnavailableCode)]
    [InlineData(WorkspaceExecutionStatus.NoWorkspace, WorkspaceExecutionErrors.UnavailableCode)]
    public void Each_reading_maps_to_one_stable_conflict_with_a_fixed_safe_description(WorkspaceExecutionStatus status, string? code)
    {
        var error = WorkspaceExecutionFence.ToError(new WorkspaceExecutionAssessment(status, Guid.NewGuid()));

        Assert.Equal(code, error?.Code);
        if (error is not null)
        {
            Assert.Equal(Devalente.Shared.Results.ErrorType.Conflict, error.Type);
            Assert.DoesNotContain(@"C:\", error.Description);
        }
    }
}
