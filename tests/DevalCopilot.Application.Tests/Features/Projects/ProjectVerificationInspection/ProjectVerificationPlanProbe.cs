using Microsoft.Data.Sqlite;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

/// <summary>Replays a recorded statement under EXPLAIN QUERY PLAN against the same database file with its recorded parameters, so a test
/// proves the real statement a handler executed is index-driven and bounded rather than asserting its text.</summary>
internal static class ProjectVerificationPlanProbe
{
    public static async Task<string> ExplainAsync(string connectionString, ProjectVerificationStatementLog.Recorded recorded)
    {
        await using var connection = new SqliteConnection(connectionString + ";Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-owned replay of a recorded statement; every value is bound as the parameter the handler bound.
        command.CommandText = "EXPLAIN QUERY PLAN " + recorded.Text;
#pragma warning restore CA2100
        foreach (var (name, value) in recorded.Parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var details = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            details.Add(reader.GetString(3));
        }

        return string.Join(" | ", details);
    }
}
