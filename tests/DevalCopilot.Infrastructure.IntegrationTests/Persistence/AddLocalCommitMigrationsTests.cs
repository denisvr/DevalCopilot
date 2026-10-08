using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// ADR-0029's four migrations against the real file-backed SQLite schema: operation storage, the seven write-seam guards of the
/// Committing reservation, the physical-receipt columns and the recreation of those guards over the whole open reservation (a
/// NeedsAttention workspace with an open operation of its own). The guards are proven one by one for the reserved workspace and for an
/// unrelated project, the guard definitions move up and down exactly, the schema moves up and down without touching other data, and the
/// EF model has no pending changes. The open-operation exclusion itself is proven against the real operation rows in the Application
/// tests (<c>LocalCommitAttentionExclusionTests</c>).
/// </summary>
public sealed class AddLocalCommitMigrationsTests : IAsyncLifetime
{
    private const string PriorMigration = "20261005171737_AddCodexAccountUsageWarning";
    private const string OperationMigration = "20261006103225_AddLocalCommitOperation";
    private const string GuardMigration = "20261006104512_AddLocalCommitSeamGuards";
    private const string ReceiptMigration = "20261006144329_AddLocalCommitIndexEffectReceipts";
    private const string AttentionMigration = "20261008090914_AddLocalCommitAttentionExclusion";
    private const string GuardMessage = "local_commit.workspace_committing";

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);
    private static readonly string[] GuardNames =
    [
        "trg_local_commit_guard_agent_attempts",
        "trg_local_commit_guard_checkpoints",
        "trg_local_commit_guard_recipes_delete",
        "trg_local_commit_guard_recipes_insert",
        "trg_local_commit_guard_recipes_update",
        "trg_local_commit_guard_reviews",
        "trg_local_commit_guard_verification_executions",
    ];

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-local-commit-migration-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    private async Task MigrateToAsync(string? target = null)
    {
        await using var context = CreateContext();
        if (target is null)
        {
            await context.Database.MigrateAsync();
        }
        else
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(target);
        }
    }

    private async Task<List<string>> ReadSchemaAsync(string type, string? like = null)
    {
        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type AND name LIKE $like ORDER BY name;";
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$like", like ?? "%");
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task<Dictionary<string, string>> ReadTriggerDefinitionsAsync()
    {
        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'trg_local_commit_%' ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync();
        var definitions = new Dictionary<string, string>();
        while (await reader.ReadAsync())
        {
            definitions[reader.GetString(0)] = reader.GetString(1);
        }

        return definitions;
    }

    private async Task<List<string>> ReadColumnsAsync(string table)
    {
        await using var probe = new SqliteConnection($"Data Source={_databasePath}");
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info($table) ORDER BY name;";
        command.Parameters.AddWithValue("$table", table);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private sealed record Scene(Project Project, Run Run, GitWorkspace Workspace, GitCheckpoint Checkpoint, VerificationCommand Command);

    private static async Task<Scene> SeedAsync(DevalCopilotDbContext context, int number)
    {
        var project = Project.Register(Guid.NewGuid(), $"Guarded {number}", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var command = VerificationCommand.Configure(
            Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), "Tests", @"C:\dotnet.exe", ["test"], 300, true, Now);
        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        context.VerificationCommands.Add(command);
        await context.SaveChangesAsync();
        return new Scene(project, run, workspace, checkpoint, command);
    }

    private static async Task SetStatusAsync(DevalCopilotDbContext context, Scene scene, WorkspaceStatus status) =>
        await context.GitWorkspaces.Where(workspace => workspace.Id == scene.Workspace.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(workspace => workspace.Status, status));

    /// <summary>One attempted write per guarded seam, each in its own fresh context so a refusal cannot taint the next.</summary>
    private static readonly (string Name, Func<DevalCopilotDbContext, Scene, Task> Write)[] Seams =
    [
        ("agent attempt", async (context, scene) =>
        {
            context.Attempts.Add(Attempt.ClaimAgentVerificationDiagnosis(
                Guid.NewGuid(), scene.Run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10),
                1024, 2048, Now, null, null, 1));
            await context.SaveChangesAsync();
        }),
        ("checkpoint", async (context, scene) =>
        {
            context.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), scene.Workspace.Id, 2, Now, new string('a', 40), new string('b', 64), []));
            await context.SaveChangesAsync();
        }),
        ("review", async (context, scene) =>
        {
            context.CheckpointReviews.Add(CheckpointReview.Record(
                Guid.NewGuid(), scene.Project.Id, scene.Workspace.Id, scene.Checkpoint.Id, 1, Fingerprint, ReviewActorKind.Human,
                ReviewDecision.Pending, Now, []));
            await context.SaveChangesAsync();
        }),
        ("verification execution", async (context, scene) =>
        {
            var workspace = await context.GitWorkspaces.SingleAsync(candidate => candidate.Id == scene.Workspace.Id);
            var checkpoint = await context.GitCheckpoints.SingleAsync(candidate => candidate.Id == scene.Checkpoint.Id);
            var command = await context.VerificationCommands.SingleAsync(candidate => candidate.Id == scene.Command.Id);
            context.VerificationExecutions.Add(VerificationExecution.Claim(
                Guid.NewGuid(), scene.Project.Id, 1, workspace, checkpoint, command, Now));
            await context.SaveChangesAsync();
        }),
        ("recipe insert", async (context, scene) =>
        {
            context.VerificationCommands.Add(VerificationCommand.Configure(
                Guid.NewGuid(), scene.Project.Id, 2, "Second", @"C:\dotnet.exe", ["test"], 300, true, Now));
            await context.SaveChangesAsync();
        }),
        ("recipe update", async (context, scene) =>
        {
            var command = await context.VerificationCommands.SingleAsync(candidate => candidate.Id == scene.Command.Id);
            command.Update("Renamed", @"C:\dotnet.exe", ["test"], 300, true, Now);
            await context.SaveChangesAsync();
        }),
        ("recipe delete", async (context, scene) =>
        {
            var command = await context.VerificationCommands.SingleAsync(candidate => candidate.Id == scene.Command.Id);
            context.VerificationCommands.Remove(command);
            await context.SaveChangesAsync();
        }),
    ];

    public static TheoryData<int> SeamIndexes => new(Enumerable.Range(0, Seams.Length));

    [Fact]
    public async Task The_tables_guards_and_receipt_columns_exist_and_the_model_has_no_pending_changes()
    {
        await MigrateToAsync();

        var tables = await ReadSchemaAsync("table");
        Assert.Contains("local_commit_operations", tables);
        Assert.Contains("local_commit_authority_members", tables);
        Assert.Equal(GuardNames, await ReadSchemaAsync("trigger", "trg_local_commit_%"));
        foreach (var (name, definition) in await ReadTriggerDefinitionsAsync())
        {
            Assert.Contains("'NeedsAttention'", definition, StringComparison.Ordinal);
            Assert.Contains("local_commit_operations", definition, StringComparison.Ordinal);
            Assert.True(definition.Contains("NOT IN ('Completed', 'Failed', 'Interrupted')", StringComparison.Ordinal), name);
        }

        var columns = await ReadColumnsAsync("local_commit_operations");
        foreach (var column in new[]
        {
            "IndexAdministrativeDirectoryIdentity", "IndexPreimageIdentity", "IndexPreimageLength", "PreparedIndexArtifactIdentity",
            "PreparedIndexArtifactLength", "IndexLockIdentity", "IndexLockLength", "IndexAcquiredAtUtc", "IndexQuarantineName",
            "IndexReplacementPlannedAtUtc",
        })
        {
            Assert.Contains(column, columns);
        }

        await using var model = CreateContext();
        Assert.False(model.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Historical_rows_survive_the_upgrade_and_no_operation_or_reservation_is_fabricated()
    {
        await MigrateToAsync(PriorMigration);
        var projectId = Guid.NewGuid();
        await using (var previous = CreateContext())
        {
            var project = Project.Register(projectId, "Historical", $@"C:\repos\{Guid.NewGuid():N}", Now);
            var workspace = GitWorkspace.Prepare(
                Guid.NewGuid(), projectId, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
            workspace.MarkReady();
            previous.Projects.Add(project);
            previous.GitWorkspaces.Add(workspace);
            await previous.SaveChangesAsync();
            await HistoricalEntityRow.InsertAsync(previous, Run.RecordIntent(Guid.NewGuid(), projectId, 1, "Historical run", Now));
        }

        await MigrateToAsync();

        await using var reopened = CreateContext();
        Assert.Equal(0, await reopened.LocalCommitOperations.CountAsync());
        Assert.Equal(0, await reopened.LocalCommitAuthorityMembers.CountAsync());
        Assert.Equal(1, await reopened.Runs.CountAsync());
        Assert.Equal(WorkspaceStatus.Ready, (await reopened.GitWorkspaces.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [MemberData(nameof(SeamIndexes))]
    public async Task Each_guard_refuses_a_committing_workspace_of_its_own_project_only_and_rolls_back(int index)
    {
        await MigrateToAsync();
        var (name, write) = Seams[index];
        Scene reserved;
        Scene unrelated;
        await using (var seed = CreateContext())
        {
            reserved = await SeedAsync(seed, 1);
            unrelated = await SeedAsync(seed, 2);
            await SetStatusAsync(seed, reserved, WorkspaceStatus.Committing);
        }

        await using (var refused = CreateContext())
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => write(refused, reserved));
            Assert.Contains(GuardMessage, exception.GetBaseException().Message, StringComparison.Ordinal);
        }

        // An unrelated repository retains its behavior. Outside the open-operation reservation no other status is guarded: these
        // workspaces have no operation, so even NeedsAttention is a historical flag (see LocalCommitAttentionExclusionTests).
        await using (var allowed = CreateContext())
        {
            await write(allowed, unrelated);
        }

        // Without an open operation only Committing is guarded: the same write is accepted for every other status, on a fresh project each.
        var number = 10;
        foreach (var status in new[] { WorkspaceStatus.Ready, WorkspaceStatus.NeedsAttention, WorkspaceStatus.AlteredExternally })
        {
            Scene other;
            await using (var seed = CreateContext())
            {
                other = await SeedAsync(seed, number++);
                await SetStatusAsync(seed, other, status);
            }

            await using var writing = CreateContext();
            await write(writing, other);
        }

        Assert.False(string.IsNullOrEmpty(name));
    }

    [Fact]
    public async Task A_refused_write_inside_a_larger_transaction_rolls_the_whole_transaction_back()
    {
        await MigrateToAsync();
        Scene reserved;
        await using (var seed = CreateContext())
        {
            reserved = await SeedAsync(seed, 1);
            await SetStatusAsync(seed, reserved, WorkspaceStatus.Committing);
        }

        await using (var context = CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Projects.Add(Project.Register(Guid.NewGuid(), "Sibling", $@"C:\repos\{Guid.NewGuid():N}", Now));
            context.GitCheckpoints.Add(GitCheckpoint.Capture(
                Guid.NewGuid(), reserved.Workspace.Id, 2, Now, new string('a', 40), new string('b', 64), []));

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.GitCheckpoints.AsNoTracking().CountAsync(checkpoint => checkpoint.WorkspaceId == reserved.Workspace.Id));
        Assert.Equal(1, await verify.Projects.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Down_to_the_guard_migration_removes_the_receipt_columns_and_the_attention_exclusion_and_keeps_every_guard_and_row()
    {
        await MigrateToAsync();
        Scene scene;
        await using (var context = CreateContext())
        {
            scene = await SeedAsync(context, 1);
        }

        await MigrateToAsync(GuardMigration);

        var columns = await ReadColumnsAsync("local_commit_operations");
        Assert.DoesNotContain("IndexPreimageIdentity", columns);
        Assert.DoesNotContain("IndexQuarantineName", columns);
        Assert.Contains("CommitSha", columns);
        Assert.Equal(GuardNames, await ReadSchemaAsync("trigger", "trg_local_commit_%"));
        Assert.All(
            (await ReadTriggerDefinitionsAsync()).Values,
            definition => Assert.DoesNotContain("NeedsAttention", definition, StringComparison.Ordinal));
        await using var verify = CreateContext();
        Assert.Equal(1, await verify.Runs.AsNoTracking().CountAsync(run => run.Id == scene.Run.Id));
    }

    [Fact]
    public async Task The_attention_exclusion_migration_recreates_every_guard_and_down_restores_the_committing_only_definitions_exactly()
    {
        await MigrateToAsync(GuardMigration);
        var original = await ReadTriggerDefinitionsAsync();
        Assert.Equal(GuardNames, original.Keys.OrderBy(name => name, StringComparer.Ordinal));

        await MigrateToAsync(AttentionMigration);
        var reserved = await ReadTriggerDefinitionsAsync();
        Assert.Equal(GuardNames, reserved.Keys.OrderBy(name => name, StringComparer.Ordinal));
        foreach (var name in GuardNames)
        {
            Assert.NotEqual(original[name], reserved[name]);
            Assert.Contains("'NeedsAttention'", reserved[name], StringComparison.Ordinal);
            Assert.Contains("'Committing'", reserved[name], StringComparison.Ordinal);
            Assert.Contains(GuardMessage, reserved[name], StringComparison.Ordinal);
        }

        await MigrateToAsync(ReceiptMigration);

        Assert.Equal(original, await ReadTriggerDefinitionsAsync());

        await MigrateToAsync();

        Assert.Equal(reserved, await ReadTriggerDefinitionsAsync());
    }

    [Fact]
    public async Task Down_to_the_operation_migration_removes_every_guard_and_down_to_the_prior_schema_removes_the_tables()
    {
        await MigrateToAsync();
        Scene scene;
        await using (var context = CreateContext())
        {
            scene = await SeedAsync(context, 1);
        }

        var tablesBefore = await ReadSchemaAsync("table");
        await MigrateToAsync(OperationMigration);
        Assert.Empty(await ReadSchemaAsync("trigger", "trg_local_commit_%"));
        Assert.Contains("local_commit_operations", await ReadSchemaAsync("table"));

        await MigrateToAsync(PriorMigration);

        var tablesAfter = await ReadSchemaAsync("table");
        Assert.Equal(
            ["local_commit_authority_members", "local_commit_operations"], tablesBefore.Except(tablesAfter).OrderBy(name => name));
        Assert.Empty(tablesAfter.Except(tablesBefore));
        Assert.Empty(await ReadSchemaAsync("trigger", "trg_local_commit_%"));
        await using var verify = CreateContext();
        Assert.Equal(1, await verify.Runs.AsNoTracking().CountAsync(run => run.Id == scene.Run.Id));
        Assert.Equal(1, await verify.GitWorkspaces.AsNoTracking().CountAsync());

        // Moving up again restores the guards exactly.
        await MigrateToAsync();
        Assert.Equal(GuardNames, await ReadSchemaAsync("trigger", "trg_local_commit_%"));
    }
}
