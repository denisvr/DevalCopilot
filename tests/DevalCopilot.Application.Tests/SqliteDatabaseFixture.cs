using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests;

/// <summary>
/// A disposable, file-backed SQLite database with production migrations applied.
/// Real EF Core query translation, not an approximation of it.
/// </summary>
public sealed class SqliteDatabaseFixture : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-app-tests-{Guid.NewGuid():N}.db");

    /// <summary>The optional <paramref name="interceptors"/> overload exists solely so a test can
    /// attach a deterministic command-counting interceptor (e.g. to prove a query path issues a
    /// fixed number of database round trips regardless of data volume) — never for anything
    /// production code depends on. Every existing no-argument call site is unaffected.</summary>
    public DevalCopilotDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DevalCopilotDbContext>()
            .UseSqlite($"Data Source={_databasePath}");

        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new DevalCopilotDbContext(optionsBuilder.Options);
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        // Scoped to this fixture's own connection string only. SqliteConnection.ClearAllPools()
        // is a process-wide operation — it clears every pooled connection for every SQLite file
        // in the whole test run, not just this one. Under xUnit's default cross-class
        // parallelism, calling it from one fixture's teardown could dispose the native sqlite3
        // handle a completely unrelated, still-running test class's connection was borrowing
        // from its own pool, surfacing as an intermittent ObjectDisposedException on
        // SQLitePCL.sqlite3 in that unrelated test. ClearPool(connection) clears only the pool
        // bucket keyed by this connection's own connection string, leaving every other fixture's
        // pool untouched. This file only needs a throwaway SqliteConnection instance to name
        // that connection string — it is never opened.
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }
}
