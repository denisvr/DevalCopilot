using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;

/// <summary>
/// ADR-0065 test support for DIRECT guarded writes: it changes one stored fact of a recorded chain through the real database, with the
/// append-only and immutability triggers of the TAMPERED table and foreign-key enforcement suspended only for that one statement and
/// restored byte-for-byte afterwards (their stored definitions are read from sqlite_master and recreated). The live-fence triggers that
/// the tests probe are never touched, so the probe below is decided by the production guard text.
/// </summary>
internal static class LocalExecutionOwnershipReleaseTamper
{
    public sealed record Ids(
        Guid RunId,
        Guid ProjectId,
        Guid WorkspaceId,
        Guid SequenceId,
        Guid StepId,
        Guid OperationId,
        Guid WitnessId,
        Guid ObservationId,
        Guid ReleaseId,
        Guid EventId,
        Guid OtherStepId,
        Guid SourceCheckpointId,
        Guid OtherCheckpointId,
        Guid? AdmissionId);

    public static async Task<Ids> ReadIdsAsync(SqliteDatabaseFixture fixture, LocalExecutionSequenceTestScene scene, LocalExecutionSequenceStep target)
    {
        await using var context = fixture.CreateContext();
        var witness = await context.LocalExecutionDispatchWitnesses.AsNoTracking().SingleAsync(candidate => candidate.StepId == target.Id);
        var observation = await context.LocalExecutionTerminationObservations.AsNoTracking().SingleAsync(candidate => candidate.WitnessId == witness.Id);
        var release = await context.LocalExecutionOwnershipReleases.AsNoTracking().SingleAsync(candidate => candidate.WitnessId == witness.Id);
        var other = await context.LocalExecutionSequenceSteps.AsNoTracking().Where(step => step.Id != target.Id).OrderBy(step => step.Ordinal)
            .Select(step => (Guid?)step.Id).FirstOrDefaultAsync();
        var checkpoint = await context.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.WorkspaceId == scene.WorkspaceId && candidate.Id != target.SourceCheckpointId)
            .OrderBy(candidate => candidate.CheckpointNumber).Select(candidate => (Guid?)candidate.Id).FirstOrDefaultAsync();
        return new Ids(
            scene.RunId, scene.ProjectId, scene.WorkspaceId, witness.SequenceId, target.Id, witness.OperationId, witness.Id, observation.Id,
            release.Id, release.EventId, other ?? Guid.NewGuid(), target.SourceCheckpointId, checkpoint ?? Guid.NewGuid(), witness.AdmissionId);
    }

    /// <summary>Executes <paramref name="sql"/> against the database with the triggers of <paramref name="table"/> and foreign keys
    /// suspended for exactly that statement. Every parameter is bound, never concatenated.</summary>
    public static Task ApplyAsync(SqliteDatabaseFixture fixture, string table, string sql, params object[] parameters) =>
        ApplyCoreAsync(fixture, table, ignoreCheckConstraints: false, sql, parameters);

    /// <summary>
    /// Like <see cref="ApplyAsync"/> but with CHECK enforcement of this one statement's connection also suspended (SQLite's own
    /// <c>ignore_check_constraints</c>, restored afterwards). It reaches rows the storage constraints keep unrepresentable, so a defense
    /// that is layered behind a CHECK or behind the typed snapshot is proved by its own boundary: a snapshot that AGREES with the changed
    /// authoritative fact must still not release.
    /// </summary>
    public static Task ApplyWithoutCheckConstraintsAsync(SqliteDatabaseFixture fixture, string table, string sql, params object[] parameters) =>
        ApplyCoreAsync(fixture, table, ignoreCheckConstraints: true, sql, parameters);

    private static async Task ApplyCoreAsync(
        SqliteDatabaseFixture fixture, string table, bool ignoreCheckConstraints, string sql, object[] parameters)
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        var triggers = new List<(string Name, string Definition)>();
        await using (var read = context.Database.GetDbConnection().CreateCommand())
        {
            read.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND tbl_name = $table;";
            var parameter = read.CreateParameter();
            parameter.ParameterName = "$table";
            parameter.Value = table;
            read.Parameters.Add(parameter);
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                triggers.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        await ExecuteAsync(context, "PRAGMA foreign_keys = OFF;");
        if (ignoreCheckConstraints)
        {
            await ExecuteAsync(context, "PRAGMA ignore_check_constraints = ON;");
        }

        try
        {
            foreach (var (name, _) in triggers)
            {
                await ExecuteAsync(context, "DROP TRIGGER \"" + name + "\";");
            }

            var changed = await context.Database.ExecuteSqlRawAsync(sql, parameters);
            Assert.True(changed >= 1, "The tampering statement changed no row: " + sql);
        }
        finally
        {
            foreach (var (_, definition) in triggers)
            {
                await ExecuteAsync(context, definition);
            }

            if (ignoreCheckConstraints)
            {
                await ExecuteAsync(context, "PRAGMA ignore_check_constraints = OFF;");
            }

            await ExecuteAsync(context, "PRAGMA foreign_keys = ON;");
        }
    }

    /// <summary>
    /// Copies one stored row of <paramref name="table"/> to a new row whose listed columns take the given values (a cross-family identity
    /// collision, for example), with the triggers of that table and foreign keys suspended for exactly that statement and restored
    /// afterwards. The column list is read from the disposable database itself; every value is bound, never concatenated.
    /// </summary>
    public static async Task CloneRowAsync(
        SqliteDatabaseFixture fixture, string table, Guid sourceId, IReadOnlyDictionary<string, object> replacements)
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        var columns = new List<string>();
        await using (var read = context.Database.GetDbConnection().CreateCommand())
        {
#pragma warning disable CA2100 // The table name is a constant of the calling test and the database is disposable.
            read.CommandText = "SELECT name FROM pragma_table_info('" + table + "');";
#pragma warning restore CA2100
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        var parameters = new List<object>();
        var selected = new List<string>();
        foreach (var column in columns)
        {
            if (replacements.TryGetValue(column, out var value))
            {
                selected.Add("{" + parameters.Count + "}");
                parameters.Add(value);
            }
            else
            {
                selected.Add("\"" + column + "\"");
            }
        }

        parameters.Add(sourceId);
        var sql = "INSERT INTO \"" + table + "\" (" + string.Join(", ", columns.Select(column => "\"" + column + "\"")) + ") SELECT "
            + string.Join(", ", selected) + " FROM \"" + table + "\" WHERE \"Id\" = {" + (parameters.Count - 1) + "}";
        var triggers = new List<(string Name, string Definition)>();
        await using (var read = context.Database.GetDbConnection().CreateCommand())
        {
            read.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND tbl_name = $table;";
            var parameter = read.CreateParameter();
            parameter.ParameterName = "$table";
            parameter.Value = table;
            read.Parameters.Add(parameter);
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                triggers.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        await ExecuteAsync(context, "PRAGMA foreign_keys = OFF;");
        try
        {
            foreach (var (name, _) in triggers)
            {
                await ExecuteAsync(context, "DROP TRIGGER \"" + name + "\";");
            }

            var changed = await context.Database.ExecuteSqlRawAsync(sql, parameters.ToArray());
            Assert.Equal(1, changed);
        }
        finally
        {
            foreach (var (_, definition) in triggers)
            {
                await ExecuteAsync(context, definition);
            }

            await ExecuteAsync(context, "PRAGMA foreign_keys = ON;");
        }
    }

    /// <summary>Runs one DDL or pragma statement whose text comes from sqlite_master or is a constant (never from a test input).</summary>
    private static async Task ExecuteAsync(DevalCopilot.Infrastructure.Persistence.DevalCopilotDbContext context, string statement)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // The statement is a constant or a definition read from sqlite_master of the disposable test database.
        command.CommandText = statement;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// The write-seam probe of the run-abandonment guard in the actual scope of the RUN (the trigger decides by the run, whatever project
    /// the run row names): the real abandonment update, inside a transaction that is always rolled back. True when the database refuses it
    /// with the manual-control refusal; false when it would have been accepted (nothing is ever kept).
    /// </summary>
    public static async Task<bool> ProbeRunAbandonmentIsBlockedAsync(SqliteDatabaseFixture fixture, Guid runId)
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE runs SET \"Lifecycle\" = 'Abandoned' WHERE \"Id\" = {0}", runId);
            return false;
        }
        catch (SqliteException exception) when (exception.Message.Contains("local_execution_sequence.manual_control_blocked", StringComparison.Ordinal))
        {
            return true;
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    /// <summary>The write-seam probe of the live-fence guards: one more verification recipe of the project. The nine ADR-0060/0062/0065
    /// triggers share one predicate, so this single statement is decided by exactly the text the migration created.</summary>
    public static async Task<bool> ProbeIsBlockedAsync(SqliteDatabaseFixture fixture, Guid projectId)
    {
        await using var context = fixture.CreateContext();
        var number = 1 + (await context.VerificationCommands.AsNoTracking().Where(candidate => candidate.ProjectId == projectId)
            .Select(candidate => (int?)candidate.CommandNumber).MaxAsync() ?? 0);
        context.VerificationCommands.Add(VerificationCommand.Configure(
            Guid.NewGuid(), projectId, number, "Probe", @"C:\dotnet.exe", ["test"], 60, true, new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero)));
        try
        {
            await context.SaveChangesAsync();
            return false;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException inner
            && inner.Message.Contains("local_execution_sequence.manual_control_blocked", StringComparison.Ordinal))
        {
            return true;
        }
    }
}
