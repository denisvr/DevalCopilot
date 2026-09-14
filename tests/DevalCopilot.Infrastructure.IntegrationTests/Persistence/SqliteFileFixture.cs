using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// A disposable, file-backed SQLite database. File-backed because SQLite is the
/// production provider; EF Core InMemory is never valid evidence here.
/// </summary>
public sealed class SqliteFileFixture : IAsyncLifetime
{
    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"devalcopilot-infra-tests-{Guid.NewGuid():N}.db");

    public DevalCopilotDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DevalCopilotDbContext>()
            .UseSqlite($"Data Source={DatabasePath}")
            .Options;

        return new DevalCopilotDbContext(options);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={DatabasePath}"));

        if (File.Exists(DatabasePath))
        {
            File.Delete(DatabasePath);
        }

        return Task.CompletedTask;
    }
}
