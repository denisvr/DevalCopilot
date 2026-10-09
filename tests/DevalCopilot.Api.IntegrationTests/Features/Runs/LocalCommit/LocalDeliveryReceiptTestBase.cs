using System.Net;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Shared plumbing for the recorded local-delivery receipt tests: a real delivered run through the production host, reads of the
/// protected receipt route, and a reversible corruption of one table at a time so a single recorded fact can be broken, observed,
/// and put back byte for byte.
/// </summary>
public abstract class LocalDeliveryReceiptTestBase : LocalCommitTestBase
{
    internal const string Route = "local-delivery-receipt";

    internal static async Task<ReceiptReading> ReceiptAsync(LocalCommitHost host, Guid runId)
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var response = await client.GetAsync($"/api/runs/{runId}/{Route}");
        return new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The exact body of an Available receipt; a comparison of two such bodies can therefore never be vacuous.</summary>
    internal static async Task<string> AvailableBodyAsync(LocalCommitHost host, Guid runId)
    {
        var reading = await ReceiptAsync(host, runId);
        Assert.Equal(HttpStatusCode.OK, reading.Status);
        Assert.Equal("Available", reading.State);
        return reading.Body;
    }

    /// <summary>Seeds the real lineage, admits the explicit commit and waits for the production supervisor to complete it.</summary>
    internal async Task<(LocalCommitLineageIds Ids, Guid OperationId)> DeliverAsync(
        LocalCommitHost host, int recipes = 1, bool completeSet = false, LocalCommitLineageIds? continueFrom = null)
    {
        var ids = await LocalCommitLineage.SeedAsync(
            host, Scene, recipes: recipes, completeSetHumanApproval: completeSet, continueFrom: continueFrom);
        var operationId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, ids.RunId, ids, operationId)).StatusCode);
        await WaitForAsync(host, ids.RunId, status => status.Operation?.Status == "Completed");
        return (ids, operationId);
    }

    internal static string Upper(Guid id) => id.ToString().ToUpperInvariant();

    internal static readonly string Zeros64 = new('0', 64);

    internal static readonly string Nines40 = new('9', 40);

    /// <summary>
    /// Backs the given tables up inside the same database file, removes their triggers, applies <paramref name="statements"/> with
    /// foreign keys relaxed, runs <paramref name="whileBroken"/>, and always puts every row and trigger back. The backup is a
    /// plain copy, so a restored table is row-for-row what it was.
    /// </summary>
    internal async Task CorruptAsync(string[] tables, string[] statements, Func<Task> whileBroken)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        var triggers = new List<(string Name, string Sql)>();
        foreach (var table in tables)
        {
            await using var read = connection.CreateCommand();
            read.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND tbl_name = $table";
            read.Parameters.AddWithValue("$table", table);
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                triggers.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF");
        foreach (var table in tables)
        {
            await ExecuteAsync(connection, $"CREATE TABLE \"bk_{table}\" AS SELECT * FROM \"{table}\"");
        }

        foreach (var (name, _) in triggers)
        {
            await ExecuteAsync(connection, $"DROP TRIGGER \"{name}\"");
        }

        try
        {
            foreach (var statement in statements)
            {
                await ExecuteAsync(connection, statement);
            }

            await whileBroken();
        }
        finally
        {
            foreach (var table in tables)
            {
                await ExecuteAsync(connection, $"DELETE FROM \"{table}\"");
                await ExecuteAsync(connection, $"INSERT INTO \"{table}\" SELECT * FROM \"bk_{table}\"");
                await ExecuteAsync(connection, $"DROP TABLE \"bk_{table}\"");
            }

            foreach (var (_, sql) in triggers)
            {
                await ExecuteAsync(connection, sql);
            }
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-only SQL assembled from constants and identifiers the test itself generated.
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A content hash of every row of every table, so a read can be proven to have written nothing at all.</summary>
    internal async Task<string> DatabaseFingerprintAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var builder = new System.Text.StringBuilder();
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Table names come from sqlite_master.
            command.CommandText = $"SELECT * FROM \"{table}\"";
#pragma warning restore CA2100
            await using var reader = await command.ExecuteReaderAsync();
            builder.Append(table).Append(':');
            while (await reader.ReadAsync())
            {
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    var value = reader.IsDBNull(column)
                        ? "<null>"
                        : Convert.ToString(reader.GetValue(column), System.Globalization.CultureInfo.InvariantCulture);
                    builder.Append(value).Append('|');
                }

                builder.Append('\n');
            }
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(builder.ToString())));
    }

    internal async Task<LocalCommitOperation> OperationByIdAsync(Guid operationId)
    {
        await using var db = OpenDb();
        return await db.LocalCommitOperations.AsNoTracking().SingleAsync(operation => operation.Id == operationId);
    }
}
