using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests;

/// <summary>
/// A disposable, file-backed SQLite database with production migrations applied.
/// Real EF Core query translation, not an approximation of it.
/// </summary>
public sealed class SqliteDatabaseFixture : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-app-tests-{Guid.NewGuid():N}.db");

    public DevalCopilotDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DevalCopilotDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        return new DevalCopilotDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }
}
