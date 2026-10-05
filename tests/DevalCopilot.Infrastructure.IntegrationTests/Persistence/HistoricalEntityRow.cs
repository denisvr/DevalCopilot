using System.Diagnostics.CodeAnalysis;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Writes one entity into a database that is deliberately stopped at an older migration, with the persisted facts the entity holds:
/// every column the current model maps for it, converted by the model's own type mappings exactly as an ordinary save would convert it,
/// omitting only the columns the table does not have at that migration. Entity Framework would also write every column of the current
/// model, including columns a later migration adds, so a migration test about an earlier schema cannot save the entity the ordinary
/// way. Nothing is invented, defaulted or dropped.
/// </summary>
internal static class HistoricalEntityRow
{
    // The only interpolated text is the table and column names of the project's own EF model (never a value, never input); every value
    // travels as a typed parameter.
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Identifiers come from the project's own EF model; all values are parameters.")]
    internal static async Task InsertAsync(DevalCopilotDbContext context, object entity)
    {
        var entityType = context.Model.FindEntityType(entity.GetType())!;
        var tableName = entityType.GetTableName()!;
        var table = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        var entry = context.Entry(entity);

        await context.Database.OpenConnectionAsync();
        var connection = context.Database.GetDbConnection();
        var available = new HashSet<string>(StringComparer.Ordinal);
        await using (var columns = connection.CreateCommand())
        {
            columns.CommandText = $"SELECT name FROM pragma_table_info('{tableName}')";
            await using var reader = await columns.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                available.Add(reader.GetString(0));
            }
        }

        await using var insert = connection.CreateCommand();
        var names = new List<string>();
        var placeholders = new List<string>();
        foreach (var property in entityType.GetProperties())
        {
            var column = property.GetColumnName(table);
            if (column is null || !available.Contains(column))
            {
                continue;
            }

            var parameterName = $"$p{names.Count}";
            var mapping = property.GetRelationalTypeMapping();
            insert.Parameters.Add(mapping.CreateParameter(insert, parameterName, entry.Property(property.Name).CurrentValue, property.IsNullable));
            names.Add($"\"{column}\"");
            placeholders.Add(parameterName);
        }

        insert.CommandText = $"INSERT INTO \"{tableName}\" ({string.Join(", ", names)}) VALUES ({string.Join(", ", placeholders)})";
        Assert.Equal(1, await insert.ExecuteNonQueryAsync());
    }
}
