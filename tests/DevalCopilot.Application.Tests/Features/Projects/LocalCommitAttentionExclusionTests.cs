using DevalCopilot.Application.Features.Projects.Commands.CaptureGitWorkspaceCheckpoint;
using DevalCopilot.Application.Features.Projects.Commands.ConfigureVerificationCommand;
using DevalCopilot.Application.Features.Projects.Commands.DeleteVerificationCommand;
using DevalCopilot.Application.Features.Projects.Commands.UpdateVerificationCommand;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies.LocalCommit;
using DevalCopilot.Application.Tests.Features.Runs;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>
/// ADR-0029 R6: the write exclusion belongs to the whole admitted, nonterminal local-commit operation, not only to the instant its
/// workspace says Committing. An ambiguous outcome turns the workspace into NeedsAttention while the operation and its run stay open,
/// and the competing writers must still be refused, both by their early check and by the database guard inside their own write
/// transaction, until the operation is proven terminal. The exclusion is bound to that operation's exact workspace and project: a
/// historical NeedsAttention workspace, a terminal operation or another project never blocks anything.
/// </summary>
public sealed class LocalCommitAttentionExclusionTests : IAsyncLifetime
{
    private const string GuardMessage = "local_commit.workspace_committing";

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    public enum Writer
    {
        Create,
        Update,
        Delete,
    }

    private sealed record Scene(LocalCommitRows Rows, VerificationCommand Recipe);

    private async Task<Scene> SeedAsync(
        LocalCommitStatus operation = LocalCommitStatus.Executing, WorkspaceStatus workspace = WorkspaceStatus.Committing)
    {
        await using var db = _fixture.CreateContext();
        // The recipe is written while the workspace is still Ready: the seam guards would refuse it afterwards.
        var rows = await LocalCommitRowsSeed.SeedAsync(db, operation, WorkspaceStatus.Ready);
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == rows.Project.Id);
        var recipe = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Tests", @"C:\dotnet.exe", ["test"], 300, true,
            LocalCommitRowsSeed.Now);
        db.VerificationCommands.Add(recipe);
        await db.SaveChangesAsync();
        if (workspace != WorkspaceStatus.Ready)
        {
            await db.GitWorkspaces.Where(candidate => candidate.Id == rows.Workspace.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, workspace));
        }

        return new Scene(rows, recipe);
    }

    private async Task<Scene> SeedAmbiguousAsync()
    {
        var scene = await SeedAsync();
        await EnterAttentionAsync(scene);
        return scene;
    }

    /// <summary>The production recorder's own transition: the operation becomes NeedsAttention and the workspace follows, while the run
    /// stays Running.</summary>
    private async Task EnterAttentionAsync(Scene scene)
    {
        await using var db = _fixture.CreateContext();
        var recorder = new LocalCommitOutcomeRecorder(db, new FixedTimeProvider(LocalCommitRowsSeed.Now.AddMinutes(1)), null);
        Assert.NotNull(await recorder.NeedsAttentionAsync(scene.Rows.Operation.Id, "local_commit.release_unproven", CancellationToken.None));
    }

    private async Task AssertAmbiguousStateAsync(Scene scene)
    {
        await using var db = _fixture.CreateContext();
        Assert.Equal(WorkspaceStatus.NeedsAttention, (await db.GitWorkspaces.AsNoTracking().SingleAsync(w => w.Id == scene.Rows.Workspace.Id)).Status);
        Assert.Equal(LocalCommitStatus.NeedsAttention, (await db.LocalCommitOperations.AsNoTracking().SingleAsync(o => o.Id == scene.Rows.Operation.Id)).Status);
        Assert.Equal(RunLifecycle.Running, (await db.Runs.AsNoTracking().SingleAsync(r => r.Id == scene.Rows.Run.Id)).Lifecycle);
    }

    private async Task AssertNothingPersistedAsync(Scene scene)
    {
        await using var db = _fixture.CreateContext();
        var recipes = await db.VerificationCommands.AsNoTracking().Where(c => c.ProjectId == scene.Rows.Project.Id).ToListAsync();
        Assert.Equal(scene.Recipe.Id, Assert.Single(recipes).Id);
        Assert.Equal("Tests", recipes[0].Name);
        Assert.Equal(2, (await db.Projects.AsNoTracking().SingleAsync(p => p.Id == scene.Rows.Project.Id)).NextVerificationCommandNumber);
        Assert.Equal(1, await db.GitCheckpoints.AsNoTracking().CountAsync(c => c.WorkspaceId == scene.Rows.Workspace.Id));
    }

    private static async Task<Devalente.Shared.Results.Error?> RunWriterAsync(
        Writer writer, Application.Data.IDevalCopilotDbContext db, Scene scene)
    {
        var time = new FixedTimeProvider(LocalCommitRowsSeed.Now);
        switch (writer)
        {
            case Writer.Create:
                var created = await new ConfigureVerificationCommandCommandHandler(db, time).HandleAsync(
                    new ConfigureVerificationCommandCommand(scene.Rows.Project.Id, "Late", @"C:\dotnet.exe", ["test"], 300, true),
                    CancellationToken.None);
                return created.IsFailure ? Assert.Single(created.Errors) : null;
            case Writer.Update:
                var updated = await new UpdateVerificationCommandCommandHandler(db, time).HandleAsync(
                    new UpdateVerificationCommandCommand(
                        scene.Rows.Project.Id, scene.Recipe.Id, "Renamed", @"C:\dotnet.exe", ["test"], 300, true),
                    CancellationToken.None);
                return updated.IsFailure ? Assert.Single(updated.Errors) : null;
            default:
                var deleted = await new DeleteVerificationCommandCommandHandler(db).HandleAsync(
                    new DeleteVerificationCommandCommand(scene.Rows.Project.Id, scene.Recipe.Id), CancellationToken.None);
                return deleted.IsFailure ? Assert.Single(deleted.Errors) : null;
        }
    }

    [Theory]
    [InlineData(Writer.Create)]
    [InlineData(Writer.Update)]
    [InlineData(Writer.Delete)]
    public async Task A_fresh_recipe_request_is_refused_while_the_operation_requires_attention(Writer writer)
    {
        var scene = await SeedAmbiguousAsync();
        await AssertAmbiguousStateAsync(scene);

        await using var db = _fixture.CreateContext();
        var error = await RunWriterAsync(writer, db, scene);

        Assert.Equal(CommitReservation.WorkspaceCommittingCode, error?.Code);
        await AssertNothingPersistedAsync(scene);
        await AssertAmbiguousStateAsync(scene);
    }

    [Theory]
    [InlineData(LocalCommitStatus.Prepared)]
    [InlineData(LocalCommitStatus.Executing)]
    [InlineData(LocalCommitStatus.NeedsAttention)]
    public async Task Every_nonterminal_operation_status_keeps_the_exclusion_over_its_attention_workspace(LocalCommitStatus status)
    {
        var scene = await SeedAsync(status, WorkspaceStatus.NeedsAttention);

        await using var db = _fixture.CreateContext();
        var error = await RunWriterAsync(Writer.Create, db, scene);

        Assert.Equal(CommitReservation.WorkspaceCommittingCode, error?.Code);
        await AssertNothingPersistedAsync(scene);
    }

    [Theory]
    [InlineData(Writer.Create)]
    [InlineData(Writer.Update)]
    [InlineData(Writer.Delete)]
    public async Task A_populated_tracker_never_hides_the_attention_reservation_from_the_early_check(Writer writer)
    {
        var scene = await SeedAsync();
        await using var db = _fixture.CreateContext();
        var trackedWorkspace = await db.GitWorkspaces.SingleAsync(w => w.Id == scene.Rows.Workspace.Id);
        var trackedOperation = await db.LocalCommitOperations.SingleAsync(o => o.Id == scene.Rows.Operation.Id);
        Assert.Equal(WorkspaceStatus.Committing, trackedWorkspace.Status);
        Assert.Equal(LocalCommitStatus.Executing, trackedOperation.Status);
        await EnterAttentionAsync(scene);

        var error = await RunWriterAsync(writer, db, scene);

        Assert.Equal(CommitReservation.WorkspaceCommittingCode, error?.Code);
        await AssertNothingPersistedAsync(scene);
    }

    private FaultInjectingDbContext AttentionBeforeSave(DevalCopilotDbContext inner, Scene scene) => new(inner)
    {
        BeforeSaveChanges = _ => EnterAttentionAsync(scene),
    };

    [Theory]
    [InlineData(Writer.Create)]
    [InlineData(Writer.Update)]
    [InlineData(Writer.Delete)]
    public async Task A_recipe_writer_that_read_a_ready_workspace_is_refused_when_the_reservation_turns_ambiguous_before_its_save(Writer writer)
    {
        // The writer's early check passed because the workspace was Ready when it read; the whole reservation then appears and becomes
        // ambiguous before the writer's own single save.
        var scene = await SeedAsync(LocalCommitStatus.Prepared, WorkspaceStatus.Ready);
        await using var inner = _fixture.CreateContext();
        var db = new FaultInjectingDbContext(inner)
        {
            BeforeSaveChanges = async _ =>
            {
                await using var competitor = _fixture.CreateContext();
                await competitor.GitWorkspaces.Where(w => w.Id == scene.Rows.Workspace.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(w => w.Status, WorkspaceStatus.Committing));
                await EnterAttentionAsync(scene);
            },
        };

        var error = await RunWriterAsync(writer, db, scene);

        Assert.Equal(CommitReservation.WorkspaceCommittingCode, error?.Code);
        await AssertNothingPersistedAsync(scene);
        await AssertAmbiguousStateAsync(scene);
    }

    private sealed class SuccessfulEvidence : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, LocalCommitRowsSeed.SourceCommit, new string('f', 64), [], string.Empty));
    }

    [Fact]
    public async Task A_checkpoint_capture_is_refused_when_attention_lands_between_its_read_and_its_save()
    {
        var scene = await SeedAsync(LocalCommitStatus.Prepared, WorkspaceStatus.Ready);
        await using var inner = _fixture.CreateContext();
        var db = new FaultInjectingDbContext(inner)
        {
            BeforeSaveChanges = async _ =>
            {
                await using var competitor = _fixture.CreateContext();
                await competitor.GitWorkspaces.Where(w => w.Id == scene.Rows.Workspace.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(w => w.Status, WorkspaceStatus.Committing));
                await EnterAttentionAsync(scene);
            },
        };
        var handler = new CaptureGitWorkspaceCheckpointCommandHandler(
            db, new SuccessfulEvidence(), new FixedTimeProvider(LocalCommitRowsSeed.Now));

        var result = await handler.HandleAsync(new CaptureGitWorkspaceCheckpointCommand(scene.Rows.Project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CommitReservation.WorkspaceCommittingCode, Assert.Single(result.Errors).Code);
        await AssertNothingPersistedAsync(scene);
    }

    /// <summary>One staged write per guarded seam. Everything is staged (and every needed row loaded) before the reservation changes, so
    /// the later save is exactly a stale writer's single write.</summary>
    private static readonly (string Name, Func<DevalCopilotDbContext, Scene, Task> Stage)[] Seams =
    [
        ("agent attempt", (context, scene) =>
        {
            // A run carries at most one attempt, so the attempt belongs to a further run of the same project.
            var run = Run.RecordIntent(Guid.NewGuid(), scene.Rows.Project.Id, 50, "Another objective", LocalCommitRowsSeed.Now);
            context.Runs.Add(run);
            context.Attempts.Add(Attempt.ClaimAgentVerificationDiagnosis(
                Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), new string('c', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, LocalCommitRowsSeed.Now, null, null, 1));
            return Task.CompletedTask;
        }),
        ("checkpoint", (context, scene) =>
        {
            context.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), scene.Rows.Workspace.Id, 2, LocalCommitRowsSeed.Now, LocalCommitRowsSeed.SourceCommit, new string('b', 64), []));
            return Task.CompletedTask;
        }),
        ("review", (context, scene) =>
        {
            context.CheckpointReviews.Add(CheckpointReview.Record(
                Guid.NewGuid(), scene.Rows.Project.Id, scene.Rows.Workspace.Id, scene.Rows.Checkpoint.Id, 1, new string('c', 64),
                ReviewActorKind.Human, ReviewDecision.Pending, LocalCommitRowsSeed.Now, []));
            return Task.CompletedTask;
        }),
        ("verification execution", async (context, scene) =>
        {
            var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.Rows.Workspace.Id);
            var checkpoint = await context.GitCheckpoints.SingleAsync(candidate => candidate.Id == scene.Rows.Checkpoint.Id);
            var command = await context.VerificationCommands.SingleAsync(candidate => candidate.Id == scene.Recipe.Id);
            context.VerificationExecutions.Add(VerificationExecution.Claim(
                Guid.NewGuid(), scene.Rows.Project.Id, 1, workspace, checkpoint, command, LocalCommitRowsSeed.Now));
        }),
        ("recipe insert", (context, scene) =>
        {
            context.VerificationCommands.Add(VerificationCommand.Configure(
                Guid.NewGuid(), scene.Rows.Project.Id, 2, "Second", @"C:\dotnet.exe", ["test"], 300, true, LocalCommitRowsSeed.Now));
            return Task.CompletedTask;
        }),
        ("recipe update", async (context, scene) =>
        {
            var command = await context.VerificationCommands.SingleAsync(candidate => candidate.Id == scene.Recipe.Id);
            command.Update("Renamed", @"C:\dotnet.exe", ["test"], 300, true, LocalCommitRowsSeed.Now);
        }),
        ("recipe delete", async (context, scene) =>
        {
            var command = await context.VerificationCommands.SingleAsync(candidate => candidate.Id == scene.Recipe.Id);
            context.VerificationCommands.Remove(command);
        }),
    ];

    public static TheoryData<int> SeamIndexes => new(Enumerable.Range(0, Seams.Length));

    [Theory]
    [MemberData(nameof(SeamIndexes))]
    public async Task Each_seam_guard_refuses_a_stale_writer_after_committing_becomes_needs_attention_and_rolls_back(int index)
    {
        var (name, stage) = Seams[index];
        var scene = await SeedAsync();
        await using var writer = _fixture.CreateContext();
        await stage(writer, scene);
        var sibling = Project.Register(Guid.NewGuid(), "Sibling " + name, $@"C:\repos\{Guid.NewGuid():N}", LocalCommitRowsSeed.Now);
        writer.Projects.Add(sibling);

        await EnterAttentionAsync(scene);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => writer.SaveChangesAsync());

        Assert.Contains(GuardMessage, exception.GetBaseException().Message, StringComparison.Ordinal);
        await using var verify = _fixture.CreateContext();
        Assert.False(await verify.Projects.AsNoTracking().AnyAsync(candidate => candidate.Id == sibling.Id), "the whole save rolled back");
        await AssertNothingPersistedAsync(scene);
        await AssertAmbiguousStateAsync(scene);
    }

    [Theory]
    [MemberData(nameof(SeamIndexes))]
    public async Task Each_seam_guard_leaves_an_unrelated_project_alone_while_another_operation_requires_attention(int index)
    {
        var (_, stage) = Seams[index];
        var ambiguous = await SeedAmbiguousAsync();
        var unrelated = await SeedAsync(LocalCommitStatus.Prepared, WorkspaceStatus.Ready);

        await using var writer = _fixture.CreateContext();
        await stage(writer, unrelated);
        await writer.SaveChangesAsync();

        await AssertAmbiguousStateAsync(ambiguous);
    }

    [Theory]
    [MemberData(nameof(SeamIndexes))]
    public async Task Each_seam_guard_accepts_a_historical_attention_workspace_whose_operation_is_terminal(int index)
    {
        var (_, stage) = Seams[index];
        // A delivered commit whose workspace was flagged afterwards: the operation is Completed, so nothing is reserved.
        var historical = await SeedAsync(LocalCommitStatus.Completed, WorkspaceStatus.NeedsAttention);

        await using var writer = _fixture.CreateContext();
        await stage(writer, historical);
        await writer.SaveChangesAsync();
    }

    [Theory]
    [InlineData(Writer.Create)]
    [InlineData(Writer.Update)]
    [InlineData(Writer.Delete)]
    public async Task Another_projects_ambiguous_operation_never_blocks_a_historical_attention_workspace(Writer writer)
    {
        await SeedAmbiguousAsync();
        var historical = await SeedAsync(LocalCommitStatus.Completed, WorkspaceStatus.NeedsAttention);

        await using var db = _fixture.CreateContext();
        var error = await RunWriterAsync(writer, db, historical);

        Assert.Null(error);
    }

    [Theory]
    [InlineData(Writer.Create)]
    [InlineData(Writer.Update)]
    [InlineData(Writer.Delete)]
    public async Task A_historical_attention_workspace_without_an_open_operation_is_not_blocked(Writer writer)
    {
        var historical = await SeedAsync(LocalCommitStatus.Interrupted, WorkspaceStatus.NeedsAttention);

        await using var db = _fixture.CreateContext();
        var error = await RunWriterAsync(writer, db, historical);

        Assert.Null(error);
    }

    [Theory]
    [InlineData(Writer.Create)]
    [InlineData(Writer.Update)]
    [InlineData(Writer.Delete)]
    public async Task A_prepared_operation_whose_workspace_is_still_ready_keeps_the_existing_ready_admission(Writer writer)
    {
        var scene = await SeedAsync(LocalCommitStatus.Prepared, WorkspaceStatus.Ready);

        await using var db = _fixture.CreateContext();
        var error = await RunWriterAsync(writer, db, scene);

        Assert.Null(error);
    }

    public enum Release
    {
        Delivered,
        ProvenUnpromotedFailure,
        ProvenUnpromotedInterruption,
    }

    [Theory]
    [InlineData(Release.Delivered)]
    [InlineData(Release.ProvenUnpromotedFailure)]
    [InlineData(Release.ProvenUnpromotedInterruption)]
    public async Task A_proven_terminal_release_ends_the_exclusion_for_every_recipe_writer(Release release)
    {
        foreach (var writer in new[] { Writer.Create, Writer.Update, Writer.Delete })
        {
            var scene = await SeedAmbiguousAsync();
            await using (var db = _fixture.CreateContext())
            {
                var recorder = new LocalCommitOutcomeRecorder(db, new FixedTimeProvider(LocalCommitRowsSeed.Now.AddMinutes(2)), null);
                _ = release switch
                {
                    Release.Delivered => await recorder.CompleteAsync(scene.Rows.Operation.Id, true, CancellationToken.None),
                    Release.ProvenUnpromotedFailure => await recorder.FailAsync(scene.Rows.Operation.Id, "local_commit.seeded", CancellationToken.None),
                    _ => await recorder.InterruptAsync(scene.Rows.Operation.Id, "local_commit.seeded", CancellationToken.None),
                };
            }

            await using var writing = _fixture.CreateContext();
            Assert.Equal(WorkspaceStatus.Ready, (await writing.GitWorkspaces.AsNoTracking().SingleAsync(w => w.Id == scene.Rows.Workspace.Id)).Status);

            var error = await RunWriterAsync(writer, writing, scene);

            Assert.Null(error);
        }
    }

    [Fact]
    public async Task A_delivery_whose_source_changed_afterwards_ends_the_exclusion_although_the_workspace_stays_flagged()
    {
        var scene = await SeedAmbiguousAsync();
        await using (var db = _fixture.CreateContext())
        {
            var recorder = new LocalCommitOutcomeRecorder(db, new FixedTimeProvider(LocalCommitRowsSeed.Now.AddMinutes(2)), null);
            await recorder.CompleteAsync(scene.Rows.Operation.Id, false, CancellationToken.None);
        }

        await using var writing = _fixture.CreateContext();
        var workspace = await writing.GitWorkspaces.AsNoTracking().SingleAsync(w => w.Id == scene.Rows.Workspace.Id);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
        Assert.Equal(LocalCommitStatus.Completed, (await writing.LocalCommitOperations.AsNoTracking().SingleAsync()).Status);

        Assert.Null(await RunWriterAsync(Writer.Create, writing, scene));
    }
}
