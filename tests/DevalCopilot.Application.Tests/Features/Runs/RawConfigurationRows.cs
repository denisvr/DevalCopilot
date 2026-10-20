using Microsoft.Data.Sqlite;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Writes configuration rows straight to storage through a connection of its own, as damaged, replaced or hand-edited storage would
/// hold them. The values are bound with their own storage class (integer, real, text or blob), so a test can place a value the typed model
/// cannot express; <paramref name="ignoreChecks"/> disables the table's check constraints for that one connection only.</summary>
internal static class RawConfigurationRows
{
    public static async Task InsertAsync(string connectionString, object revision, object limit, bool ignoreChecks)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        if (ignoreChecks)
        {
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA ignore_check_constraints = ON;";
            await pragma.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO execution_capacity_configurations (Revision, \"Limit\") VALUES ($revision, $limit);";
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$limit", limit);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task DeleteAllAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM execution_capacity_configurations;";
        await command.ExecuteNonQueryAsync();
    }
}
