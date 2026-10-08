using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetCheckpointApprovalEvidence;
using DevalCopilot.Application.Tests.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// The read-only complete approval bundle (ADR-0030) over a real file-backed SQLite database: it is derived from the enabled
/// recipes and their latest bound executions, never from a bounded history page, it is refused with a fixed code whenever it is not
/// complete and current, and it writes nothing.
/// </summary>
public sealed class GetCheckpointApprovalEvidenceQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('b', 64);
    private static readonly string OtherFingerprint = new('c', 64);
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task The_bundle_names_the_source_and_every_enabled_recipe_in_command_order_with_its_latest_passed_execution()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var result = await QueryAsync(dbContext, scene);

        Assert.True(result.IsSuccess);
        var bundle = result.Value;
        Assert.Equal((scene.Project.Id, scene.Workspace.Id, scene.Checkpoint.Id, 2, Fingerprint),
            (bundle.ProjectId, bundle.WorkspaceId, bundle.CheckpointId, bundle.CheckpointNumber, bundle.FingerprintSha256));
        Assert.Collection(
            bundle.Members,
            first =>
            {
                Assert.Equal((scene.Unit.Id, 1, "Unit", scene.UnitPassed.Id, 1), (first.VerificationCommandId, first.CommandNumber, first.RecipeLabel, first.VerificationExecutionId, first.ExecutionNumber));
            },
            second =>
            {
                Assert.Equal((scene.Lint.Id, 2, "Lint", scene.LintPassed.Id, 3), (second.VerificationCommandId, second.CommandNumber, second.RecipeLabel, second.VerificationExecutionId, second.ExecutionNumber));
            });
    }

    [Fact]
    public async Task The_bundle_is_not_derived_from_a_latest_twenty_window_of_history()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        for (var number = 10; number < 60; number++)
        {
            await AddAsync(dbContext, scene, number % 2 == 0 ? scene.Unit : scene.Lint, number, VerificationExecutionStatus.Failed, Fingerprint, completeFingerprint: Fingerprint);
        }

        var stillFailed = await QueryAsync(dbContext, scene);
        Assert.Equal(GetCheckpointApprovalEvidenceQueryHandler.IncompleteCode, Assert.Single(stillFailed.Errors).Code);

        var latestUnit = await AddAsync(dbContext, scene, scene.Unit, 70, VerificationExecutionStatus.Passed, Fingerprint, completeFingerprint: Fingerprint);
        var latestLint = await AddAsync(dbContext, scene, scene.Lint, 71, VerificationExecutionStatus.Passed, Fingerprint, completeFingerprint: Fingerprint);
        var recovered = await QueryAsync(dbContext, scene);

        Assert.True(recovered.IsSuccess);
        Assert.Equal(
            [latestUnit.Id, latestLint.Id],
            recovered.Value.Members.Select(member => member.VerificationExecutionId));
        Assert.True(await dbContext.VerificationExecutions.CountAsync(execution => execution.ProjectId == scene.Project.Id) > 20);
    }

    [Fact]
    public async Task A_recipe_whose_latest_execution_left_the_latest_twenty_window_is_still_in_the_bundle()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        // Lint's only Passed execution is number 3; thirty newer executions of Unit push it out of any latest-20 page of the history.
        for (var number = 10; number < 40; number++)
        {
            await AddAsync(dbContext, scene, scene.Unit, number, VerificationExecutionStatus.Passed, Fingerprint, completeFingerprint: Fingerprint);
        }

        var result = await QueryAsync(dbContext, scene);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [(scene.Unit.Id, 39), (scene.Lint.Id, 3)],
            result.Value.Members.Select(member => (member.VerificationCommandId, member.ExecutionNumber)));
        Assert.Equal(scene.LintPassed.Id, result.Value.Members[1].VerificationExecutionId);
    }

    [Fact]
    public async Task The_query_writes_nothing_and_never_saves()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        var before = await CountsAsync();
        var faulting = new FaultInjectingDbContext(dbContext)
        {
            BeforeSaveChanges = _ => throw new InvalidOperationException("the query must never save"),
            BeforeBeginTransaction = _ => throw new InvalidOperationException("the query must never open a transaction"),
        };

        var result = await new GetCheckpointApprovalEvidenceQueryHandler(faulting, new FixedReader(Fingerprint)).HandleAsync(
            new GetCheckpointApprovalEvidenceQuery(scene.Project.Id, scene.Checkpoint.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task The_observation_runs_once_outside_any_transaction()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        var reader = new FixedReader(Fingerprint, () => Assert.Null(dbContext.Database.CurrentTransaction));

        var result = await new GetCheckpointApprovalEvidenceQueryHandler(dbContext, reader).HandleAsync(
            new GetCheckpointApprovalEvidenceQuery(scene.Project.Id, scene.Checkpoint.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, reader.Captures);
    }

    [Fact]
    public async Task A_source_that_no_longer_matches_the_checkpoint_fingerprint_is_not_offered()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var result = await new GetCheckpointApprovalEvidenceQueryHandler(dbContext, new FixedReader(OtherFingerprint)).HandleAsync(
            new GetCheckpointApprovalEvidenceQuery(scene.Project.Id, scene.Checkpoint.Id), CancellationToken.None);

        Assert.Equal("reviews.checkpoint_not_current", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task A_failed_observation_is_not_offered()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var result = await new GetCheckpointApprovalEvidenceQueryHandler(
            dbContext, new FixedReader(Fingerprint, outcome: GitWorkspaceEvidenceOutcome.GitInvocationFailed)).HandleAsync(
            new GetCheckpointApprovalEvidenceQuery(scene.Project.Id, scene.Checkpoint.Id), CancellationToken.None);

        Assert.Equal("reviews.checkpoint_not_current", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task A_historical_checkpoint_is_not_offered()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var result = await new GetCheckpointApprovalEvidenceQueryHandler(dbContext, new FixedReader(Fingerprint)).HandleAsync(
            new GetCheckpointApprovalEvidenceQuery(scene.Project.Id, scene.OldCheckpoint.Id), CancellationToken.None);

        Assert.Equal("reviews.checkpoint_not_current", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task A_foreign_checkpoint_of_another_project_is_not_offered()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var result = await new GetCheckpointApprovalEvidenceQueryHandler(dbContext, new FixedReader(Fingerprint)).HandleAsync(
            new GetCheckpointApprovalEvidenceQuery(scene.Project.Id, scene.ForeignCheckpoint.Id), CancellationToken.None);

        Assert.Equal("reviews.checkpoint_not_current", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task An_unknown_project_is_not_found()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var result = await new GetCheckpointApprovalEvidenceQueryHandler(dbContext, new FixedReader(Fingerprint)).HandleAsync(
            new GetCheckpointApprovalEvidenceQuery(Guid.NewGuid(), scene.Checkpoint.Id), CancellationToken.None);

        Assert.Equal("reviews.not_found", Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData(WorkspaceStatus.Committing)]
    [InlineData(WorkspaceStatus.NeedsAttention)]
    public async Task A_reserved_or_unready_workspace_is_not_offered(WorkspaceStatus status)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        await dbContext.GitWorkspaces.Where(workspace => workspace.Id == scene.Workspace.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(workspace => workspace.Status, status));

        var result = await QueryAsync(dbContext, scene);

        Assert.Equal("reviews.workspace_not_ready", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task A_lost_lease_is_not_offered()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        await dbContext.RepositoryMutationLeases.ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released));

        var result = await QueryAsync(dbContext, scene);

        Assert.Equal("reviews.workspace_not_ready", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task No_enabled_recipe_is_refused_with_its_own_code()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        await dbContext.VerificationCommands.Where(command => command.ProjectId == scene.Project.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(command => command.IsEnabled, false));

        var result = await QueryAsync(dbContext, scene);

        Assert.Equal(GetCheckpointApprovalEvidenceQueryHandler.NoEnabledRecipesCode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task More_than_thirty_two_enabled_recipes_are_refused_never_truncated()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        for (var number = 10; number < 40; number++)
        {
            var command = VerificationCommand.Configure(Guid.NewGuid(), scene.Project.Id, number, $"Recipe {number}", @"C:\dotnet.exe", ["x"], 60, true, Now);
            dbContext.VerificationCommands.Add(command);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await AddAsync(dbContext, scene, command, 100 + number, VerificationExecutionStatus.Passed, Fingerprint, completeFingerprint: Fingerprint);
        }

        var thirtyTwo = await QueryAsync(dbContext, scene);
        Assert.True(thirtyTwo.IsSuccess);
        Assert.Equal(32, thirtyTwo.Value.Members.Count);

        var thirtyThird = VerificationCommand.Configure(Guid.NewGuid(), scene.Project.Id, 50, "Recipe 50", @"C:\dotnet.exe", ["x"], 60, true, Now);
        dbContext.VerificationCommands.Add(thirtyThird);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await AddAsync(dbContext, scene, thirtyThird, 190, VerificationExecutionStatus.Passed, Fingerprint, completeFingerprint: Fingerprint);

        var refused = await QueryAsync(dbContext, scene);

        Assert.Equal(GetCheckpointApprovalEvidenceQueryHandler.TooManyRecipesCode, Assert.Single(refused.Errors).Code);
    }

    [Fact]
    public async Task A_recipe_without_an_execution_for_the_checkpoint_is_incomplete()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        dbContext.VerificationCommands.Add(
            VerificationCommand.Configure(Guid.NewGuid(), scene.Project.Id, 4, "Format", @"C:\dotnet.exe", ["format"], 60, true, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await QueryAsync(dbContext, scene);

        Assert.Equal(GetCheckpointApprovalEvidenceQueryHandler.IncompleteCode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task A_running_latest_execution_is_incomplete()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        dbContext.VerificationExecutions.Add(
            VerificationExecution.Claim(Guid.NewGuid(), scene.Project.Id, 60, scene.Workspace, scene.Checkpoint, scene.Lint, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await QueryAsync(dbContext, scene);

        Assert.Equal(GetCheckpointApprovalEvidenceQueryHandler.IncompleteCode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task A_newer_failed_latest_never_falls_back_to_an_older_pass()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        await AddAsync(dbContext, scene, scene.Unit, 30, VerificationExecutionStatus.Failed, Fingerprint, completeFingerprint: Fingerprint);

        var result = await QueryAsync(dbContext, scene);

        Assert.Equal(GetCheckpointApprovalEvidenceQueryHandler.IncompleteCode, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData("recipe-timeout")]
    [InlineData("recipe-arguments")]
    [InlineData("completion-fingerprint")]
    [InlineData("workspace-path")]
    [InlineData("exit-code")]
    [InlineData("not-dispatched")]
    public async Task An_execution_that_is_not_a_coherent_clean_pass_of_the_current_recipe_is_incomplete(string tamper)
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        switch (tamper)
        {
            case "recipe-timeout":
                await dbContext.VerificationCommands.Where(command => command.Id == scene.Unit.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(command => command.TimeoutSeconds, 61));
                break;
            case "recipe-arguments":
                await using (var other = fixture.CreateContext())
                {
                    var recipe = await other.VerificationCommands.SingleAsync(command => command.Id == scene.Unit.Id);
                    recipe.Update(recipe.Name, recipe.ExecutablePath, ["other"], recipe.TimeoutSeconds, true, Now.AddMinutes(1));
                    await other.SaveChangesAsync(CancellationToken.None);
                }

                break;
            case "completion-fingerprint":
                await dbContext.VerificationExecutions.Where(execution => execution.Id == scene.LintPassed.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.CompletionFingerprintSha256, OtherFingerprint));
                break;
            case "workspace-path":
                await dbContext.VerificationExecutions.Where(execution => execution.Id == scene.UnitPassed.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.WorkspacePath, @"C:\elsewhere"));
                break;
            case "exit-code":
                await dbContext.VerificationExecutions.Where(execution => execution.Id == scene.UnitPassed.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.ExitCode, 3));
                break;
            default:
                await dbContext.VerificationExecutions.Where(execution => execution.Id == scene.UnitPassed.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.DispatchedAtUtc, (DateTimeOffset?)null));
                break;
        }

        var result = await QueryAsync(dbContext, scene);

        Assert.Equal(GetCheckpointApprovalEvidenceQueryHandler.IncompleteCode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task A_disabled_recipe_and_another_checkpoint_never_enter_the_bundle()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);

        var result = await QueryAsync(dbContext, scene);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Value.Members, member => member.VerificationCommandId == scene.Disabled.Id);
        Assert.DoesNotContain(result.Value.Members, member => member.VerificationExecutionId == scene.OldCheckpointPassed.Id);
    }

    [Fact]
    public async Task The_refusals_are_fixed_and_never_echo_stored_values()
    {
        await using var dbContext = fixture.CreateContext();
        var scene = await SeedAsync(dbContext);
        await dbContext.VerificationCommands.Where(command => command.Id == scene.Unit.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(command => command.Name, "SENTINEL-recipe-name-C:\\secret"));

        var result = await QueryAsync(dbContext, scene);

        var error = Assert.Single(result.Errors);
        Assert.DoesNotContain("SENTINEL", error.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", error.Description, StringComparison.Ordinal);
    }

    private async Task<(int Reviews, int Members, int Executions, int Commands, int Workspaces)> CountsAsync()
    {
        await using var verify = fixture.CreateContext();
        return (
            await verify.CheckpointReviews.CountAsync(),
            await verify.CheckpointReviewEvidence.CountAsync(),
            await verify.VerificationExecutions.CountAsync(),
            await verify.VerificationCommands.CountAsync(),
            await verify.GitWorkspaces.CountAsync());
    }

    private static Task<Result<CheckpointApprovalEvidenceQueryResult>> QueryAsync(DevalCopilotDbContext dbContext, Scene scene) =>
        new GetCheckpointApprovalEvidenceQueryHandler(dbContext, new FixedReader(Fingerprint)).HandleAsync(
            new GetCheckpointApprovalEvidenceQuery(scene.Project.Id, scene.Checkpoint.Id), CancellationToken.None);

    private sealed record Scene(
        Project Project,
        GitWorkspace Workspace,
        GitCheckpoint OldCheckpoint,
        GitCheckpoint Checkpoint,
        VerificationCommand Unit,
        VerificationCommand Lint,
        VerificationCommand Disabled,
        VerificationExecution UnitPassed,
        VerificationExecution LintPassed,
        VerificationExecution OldCheckpointPassed,
        GitCheckpoint ForeignCheckpoint);

    private static async Task<Scene> SeedAsync(DevalCopilotDbContext dbContext)
    {
        var project = Project.Register(Guid.NewGuid(), "Review project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch-1", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var oldCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), OtherFingerprint, []);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now, new string('a', 40), Fingerprint, []);
        var unit = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Unit", @"C:\dotnet.exe", ["test"], 60, true, Now);
        var lint = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 2, "Lint", @"C:\dotnet.exe", ["format"], 60, true, Now);
        var disabled = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 3, "Disabled", @"C:\dotnet.exe", ["x"], 60, false, Now);
        var unitPassed = Terminal(project, workspace, checkpoint, unit, 1, VerificationExecutionStatus.Passed, Fingerprint);
        var lintFailed = Terminal(project, workspace, checkpoint, lint, 2, VerificationExecutionStatus.Failed, Fingerprint);
        var lintPassed = Terminal(project, workspace, checkpoint, lint, 3, VerificationExecutionStatus.Passed, Fingerprint);
        var disabledPassed = Terminal(project, workspace, checkpoint, disabled, 4, VerificationExecutionStatus.Passed, Fingerprint);
        var oldPassed = Terminal(project, workspace, oldCheckpoint, unit, 5, VerificationExecutionStatus.Passed, OtherFingerprint);

        var otherProject = Project.Register(Guid.NewGuid(), "Other project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var otherWorkspace = GitWorkspace.Prepare(
            Guid.NewGuid(), otherProject.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch-x", new string('a', 40), "main", Now);
        otherWorkspace.MarkReady();
        var otherCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), otherWorkspace.Id, 1, Now, new string('a', 40), Fingerprint, []);

        dbContext.Projects.AddRange(project, otherProject);
        dbContext.GitWorkspaces.AddRange(workspace, otherWorkspace);
        dbContext.GitCheckpoints.AddRange(oldCheckpoint, checkpoint, otherCheckpoint);
        dbContext.VerificationCommands.AddRange(unit, lint, disabled);
        dbContext.RepositoryMutationLeases.AddRange(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now),
            RepositoryMutationLease.Acquire(Guid.NewGuid(), otherProject.Id, otherWorkspace.Id, 1, Guid.NewGuid().ToByteArray(), Now));
        dbContext.VerificationExecutions.AddRange(unitPassed, lintFailed, lintPassed, disabledPassed, oldPassed);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return new Scene(project, workspace, oldCheckpoint, checkpoint, unit, lint, disabled, unitPassed, lintPassed, oldPassed, otherCheckpoint);
    }

    private static async Task<VerificationExecution> AddAsync(
        DevalCopilotDbContext dbContext,
        Scene scene,
        VerificationCommand command,
        int number,
        VerificationExecutionStatus status,
        string checkpointFingerprint,
        string completeFingerprint)
    {
        var execution = Terminal(scene.Project, scene.Workspace, scene.Checkpoint, command, number, status, completeFingerprint);
        Assert.Equal(checkpointFingerprint, scene.Checkpoint.FingerprintSha256);
        dbContext.VerificationExecutions.Add(execution);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return execution;
    }

    private static VerificationExecution Terminal(
        Project project,
        GitWorkspace workspace,
        GitCheckpoint checkpoint,
        VerificationCommand command,
        int number,
        VerificationExecutionStatus status,
        string completionFingerprint)
    {
        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, number, workspace, checkpoint, command, Now);
        execution.MarkDispatched(Now);
        execution.Complete(VerificationExecutionOutcome.Exited, status == VerificationExecutionStatus.Passed ? 0 : 1, completionFingerprint, Now);
        return execution;
    }

    private sealed class FixedReader(
        string fingerprint, Action? onCapture = null, GitWorkspaceEvidenceOutcome outcome = GitWorkspaceEvidenceOutcome.Success)
        : IGitWorkspaceEvidenceReader
    {
        public int Captures { get; private set; }

        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            Captures++;
            onCapture?.Invoke();
            return Task.FromResult(new GitWorkspaceEvidenceResult(outcome, new string('a', 40), fingerprint, [], null));
        }
    }
}
