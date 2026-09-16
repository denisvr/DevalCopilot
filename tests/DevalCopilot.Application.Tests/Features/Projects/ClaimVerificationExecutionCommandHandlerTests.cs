using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;
using DevalCopilot.Application.Features.Projects.Commands.ReconcileInterruptedVerificationExecutions;
using DevalCopilot.Application.Features.Projects.Commands.RecordVerificationExecutionSourceChanged;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

public sealed class ClaimVerificationExecutionCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 15, 0, 0, TimeSpan.Zero);
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Claim_requires_current_checkpoint_and_snapshots_the_enabled_recipe()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, workspace, checkpoint, recipe) = await AddReadyWorkspaceAsync(dbContext);
        var handler = new ClaimVerificationExecutionCommandHandler(
            dbContext,
            new FixedEvidenceReader(checkpoint.FingerprintSha256),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new ClaimVerificationExecutionCommand(project.Id, recipe.Id, checkpoint.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var execution = Assert.Single(dbContext.VerificationExecutions);
        Assert.Equal(workspace.Id, execution.GitWorkspaceId);
        Assert.Equal(recipe.Arguments, execution.Arguments);
        Assert.Equal(VerificationExecutionStatus.Running, execution.Status);
    }

    [Fact]
    public async Task Claim_rejects_drifted_checkpoint_without_creating_an_execution()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, _, checkpoint, recipe) = await AddReadyWorkspaceAsync(dbContext);
        var handler = new ClaimVerificationExecutionCommandHandler(
            dbContext,
            new FixedEvidenceReader(new string('c', 64)),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new ClaimVerificationExecutionCommand(project.Id, recipe.Id, checkpoint.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("verification.checkpoint_not_current", result.Errors[0].Code);
        Assert.Empty(dbContext.VerificationExecutions);
    }

    [Fact]
    public async Task Claim_rejects_a_disabled_recipe()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, _, checkpoint, recipe) = await AddReadyWorkspaceAsync(dbContext, recipeEnabled: false);
        var handler = new ClaimVerificationExecutionCommandHandler(
            dbContext,
            new FixedEvidenceReader(checkpoint.FingerprintSha256),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(
            new ClaimVerificationExecutionCommand(project.Id, recipe.Id, checkpoint.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("verification.disabled", result.Errors[0].Code);
    }

    [Fact]
    public async Task Claim_requires_a_ready_workspace_and_active_lease()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), new string('b', 64), []);
        var recipe = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Tests", @"C:\dotnet.exe", ["test"], 60, true, Now);
        dbContext.Projects.Add(project);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.VerificationCommands.Add(recipe);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ClaimVerificationExecutionCommandHandler(
            dbContext,
            new FixedEvidenceReader(checkpoint.FingerprintSha256),
            new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new ClaimVerificationExecutionCommand(project.Id, recipe.Id, checkpoint.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("verification.workspace_not_ready", result.Errors[0].Code);
    }

    [Fact]
    public async Task Dispatch_marker_is_single_use_and_restart_reconciliation_interrupts_without_redispatch()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, _, checkpoint, recipe) = await AddReadyWorkspaceAsync(dbContext);
        var claimHandler = new ClaimVerificationExecutionCommandHandler(
            dbContext,
            new FixedEvidenceReader(checkpoint.FingerprintSha256),
            new FixedTimeProvider(Now));
        var claimed = await claimHandler.HandleAsync(
            new ClaimVerificationExecutionCommand(project.Id, recipe.Id, checkpoint.Id), CancellationToken.None);
        Assert.True(claimed.IsSuccess);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var dispatchHandler = new MarkVerificationExecutionDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var firstDispatch = await dispatchHandler.HandleAsync(
            new MarkVerificationExecutionDispatchedCommand(claimed.Value.VerificationExecutionId), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var secondDispatch = await dispatchHandler.HandleAsync(
            new MarkVerificationExecutionDispatchedCommand(claimed.Value.VerificationExecutionId), CancellationToken.None);

        Assert.True(firstDispatch.IsSuccess);
        Assert.True(secondDispatch.IsFailure);
        Assert.Equal("verification.execution_already_dispatched", secondDispatch.Errors[0].Code);

        var reconcileHandler = new ReconcileInterruptedVerificationExecutionsCommandHandler(dbContext, new FixedTimeProvider(Now));
        var reconciled = await reconcileHandler.HandleAsync(new ReconcileInterruptedVerificationExecutionsCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(reconciled.IsSuccess);
        Assert.Equal(1, reconciled.Value);
        Assert.Equal(VerificationExecutionStatus.Interrupted, dbContext.VerificationExecutions.Single().Status);
    }

    [Fact]
    public async Task Source_change_rejection_does_not_disclose_the_domain_exception()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, _, checkpoint, recipe) = await AddReadyWorkspaceAsync(dbContext);
        var claimHandler = new ClaimVerificationExecutionCommandHandler(
            dbContext,
            new FixedEvidenceReader(checkpoint.FingerprintSha256),
            new FixedTimeProvider(Now));
        var claimed = await claimHandler.HandleAsync(
            new ClaimVerificationExecutionCommand(project.Id, recipe.Id, checkpoint.Id), CancellationToken.None);
        Assert.True(claimed.IsSuccess);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var dispatchHandler = new MarkVerificationExecutionDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var dispatched = await dispatchHandler.HandleAsync(
            new MarkVerificationExecutionDispatchedCommand(claimed.Value.VerificationExecutionId), CancellationToken.None);
        Assert.True(dispatched.IsSuccess);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await dbContext.Entry(dbContext.VerificationExecutions.Single()).ReloadAsync(CancellationToken.None);

        var handler = new RecordVerificationExecutionSourceChangedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new RecordVerificationExecutionSourceChangedCommand(claimed.Value.VerificationExecutionId, new string('c', 64)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("verification.execution_not_pending", result.Errors[0].Code);
        Assert.Equal("This verification execution is no longer pending.", result.Errors[0].Description);
        Assert.DoesNotContain("Only an undispatched running verification execution can be invalidated.", result.Errors[0].Description, StringComparison.Ordinal);
    }

    private static async Task<(Project Project, GitWorkspace Workspace, GitCheckpoint Checkpoint, VerificationCommand Recipe)> AddReadyWorkspaceAsync(
        DevalCopilotDbContext dbContext, bool recipeEnabled = true)
    {
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), new string('b', 64), []);
        var recipe = VerificationCommand.Configure(Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Tests", @"C:\dotnet.exe", ["test"], 60, recipeEnabled, Now);
        dbContext.Projects.Add(project);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.VerificationCommands.Add(recipe);
        dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, new byte[16], Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return (project, workspace, checkpoint, recipe);
    }

    private sealed class FixedEvidenceReader(string fingerprintSha256) : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null));
    }
}
