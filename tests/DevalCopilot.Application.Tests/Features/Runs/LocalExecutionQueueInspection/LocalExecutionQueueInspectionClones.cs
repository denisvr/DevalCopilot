using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionQueueInspection;

/// <summary>
/// The feature-owned bounded fixture of the ADR-0064 count tests. A cycle created by the production commands is the template (one
/// Waiting admission and no step, a Reserved admission with its Reserved step, or an ended Unresolved reservation with its Unresolved
/// step); each clone copies its whole row graph with fresh identities, a project label and the next admission and coordination numbers,
/// so 68 coherent cycles exist without running the provider workflow 68 times. This is a TEST-ONLY storage write through a connection of
/// its own: the production arming, queue-bound and insert guards are dropped (they would refuse a clone that has no plan message of its
/// own) while foreign keys, checks and every unique index stay on, so every clone is referentially and structurally coherent.
/// </summary>
internal static class LocalExecutionQueueInspectionClones
{
    private static readonly string[] BypassedGuards =
    [
        "trg_local_execution_guard_arming", "trg_local_execution_guard_tuple", "trg_local_execution_queue_bound",
        "trg_local_execution_admission_insert_shape", "trg_local_execution_admission_insert_bound",
        "trg_local_execution_step_admission", "trg_local_execution_step_bounds",
    ];

    public static async Task<List<ClonedCycle>> CloneAsync(
        SqliteDatabaseFixture fixture, LocalExecutionSequenceTestScene template, int count, string namePrefix)
    {
        var sequence = await template.ReadSequenceAsync();
        var admission = Assert.Single(await template.ReadAdmissionsAsync());
        var step = (await template.ReadStepsAsync()).SingleOrDefault();
        var clones = new List<ClonedCycle>();
        await using var connection = new SqliteConnection(fixture.ConnectionString + ";Pooling=False");
        await connection.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON; " + string.Concat(BypassedGuards.Select(guard => $"DROP TRIGGER IF EXISTS {guard};")));
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        for (var index = 1; index <= count; index++)
        {
            var clone = new ClonedCycle(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), $"{namePrefix} {index}");
            var workspace = Guid.NewGuid();
            var checkpoint = Guid.NewGuid();
            var newStep = step is null ? (Guid?)null : Guid.NewGuid();
            await ExecuteAsync(
                connection,
                CopyRow("projects", "Id = $tp", "Id = $np, Name = $name, RegistrationIdentityKey = $key")
                + CopyRow("runs", "Id = $tr", "Id = $nr, ProjectId = $np")
                + CopyRow("git_workspaces", "Id = $tw", "Id = $nw, ProjectId = $np")
                + CopyRow("git_checkpoints", "Id = $tc", "Id = $nc, WorkspaceId = $nw")
                + CopyRow(
                    "local_execution_sequences",
                    "Id = $ts",
                    "Id = $ns, RunId = $nr, ProjectId = $np, WorkspaceId = $nw, CheckpointId = $nc, "
                    + "CoordinationNumber = (SELECT MAX(CoordinationNumber) + 1 FROM local_execution_sequences)")
                + CopyRows(
                    "local_execution_sequence_recipes",
                    "SequenceId = $ts",
                    "Id = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4000-8000-' || hex(randomblob(6))), SequenceId = $ns")
                + (step is null
                    ? string.Empty
                    : CopyRow(
                        "local_execution_sequence_steps",
                        "Id = $tstep",
                        "Id = $nstep, SequenceId = $ns, SourceCheckpointId = $nc, ReservedOperationId = $nop"))
                + CopyRow(
                    "local_execution_admissions",
                    "Id = $ta",
                    "Id = $na, SequenceId = $ns, SourceCheckpointId = $nc, StepId = " + (step is null ? "NULL" : "$nstep")
                    + ", AdmissionNumber = (SELECT MAX(AdmissionNumber) + 1 FROM local_execution_admissions)"),
                ("$tp", template.ProjectId), ("$tr", template.RunId), ("$tw", template.WorkspaceId), ("$tc", template.CheckpointId),
                ("$ts", sequence.Id), ("$ta", admission.Id), ("$tstep", step?.Id), ("$np", clone.ProjectId), ("$nr", clone.RunId),
                ("$nw", workspace), ("$nc", checkpoint), ("$ns", clone.SequenceId), ("$na", clone.AdmissionId), ("$nstep", newStep),
                ("$nop", Guid.NewGuid()), ("$name", clone.ProjectName), ("$key", $"clone-{Guid.NewGuid():N}"));
            clones.Add(clone);
        }

        await transaction.CommitAsync();
        return clones;
    }

    private static string CopyRow(string table, string where, string set) => CopyRows(table, where, set);

    private static string CopyRows(string table, string where, string set) =>
        $"CREATE TEMP TABLE c_{table} AS SELECT * FROM {table} WHERE {where}; UPDATE c_{table} SET {set}; "
        + $"INSERT INTO {table} SELECT * FROM c_{table}; DROP TABLE c_{table}; ";

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] values)
    {
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-owned statement text; every value is bound as a parameter.
        command.CommandText = sql;
#pragma warning restore CA2100
        foreach (var (name, value) in values)
        {
            command.Parameters.AddWithValue(name, value switch
            {
                Guid guid => guid.ToString("D").ToUpperInvariant(),
                null => DBNull.Value,
                _ => value,
            });
        }

        await command.ExecuteNonQueryAsync();
    }
}
