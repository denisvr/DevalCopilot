using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves the <c>AddProjectRegistration</c> migration's legacy-data handling directly: seeds a
/// database at the schema shape immediately before that migration (only <c>Id</c>,
/// <c>Name</c>, <c>CanonicalPath</c>, <c>NextExecutionNumber</c> on <c>projects</c>), inserts a
/// raw legacy row, then applies the migration and observes the result — never against the real
/// application database.
/// </summary>
public sealed class RegisterProjectMigrationTests : IAsyncLifetime
{
    private const string PreProjectRegistrationMigration = "20260914095140_AddProcessOutputArtifacts";
    private const string KnownLegacyCanonicalPath = @"C:\repos\DevalCopilot";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-legacy-migration-{Guid.NewGuid():N}.db");

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

    private async Task MigrateToPreRegistrationShapeAsync()
    {
        await using var context = CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreProjectRegistrationMigration);
    }

    private async Task InsertLegacyProjectRowAsync(string canonicalPath)
    {
        await using var context = CreateContext();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        await context.Database.OpenConnectionAsync();
        command.CommandText =
            "INSERT INTO projects (Id, Name, CanonicalPath, NextExecutionNumber) VALUES ($id, $name, $path, 1);";
        command.Parameters.Add(new SqliteParameter("$id", Guid.NewGuid().ToString()));
        command.Parameters.Add(new SqliteParameter("$name", "Legacy"));
        command.Parameters.Add(new SqliteParameter("$path", canonicalPath));
        await command.ExecuteNonQueryAsync();
    }

    private async Task MigrateToLatestAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    [Fact]
    public async Task Migrating_backfills_the_known_legacy_fixture_row_truthfully_with_no_fabricated_data()
    {
        await MigrateToPreRegistrationShapeAsync();
        await InsertLegacyProjectRowAsync(KnownLegacyCanonicalPath);

        await MigrateToLatestAsync();

        await using var context = CreateContext();
        var project = await context.Projects.SingleAsync(p => p.CanonicalPath == KnownLegacyCanonicalPath);

        Assert.Equal(KnownLegacyCanonicalPath.ToUpperInvariant(), project.RegistrationIdentityKey);
        // Never a fabricated historical date — the true registration time of a legacy row that
        // predates this feature is genuinely unknown.
        Assert.Null(project.RegisteredAtUtc);
        // No synthetic baseline: this project was never actually inspected.
        Assert.Empty(await context.RepositoryBaselines.Where(b => b.ProjectId == project.Id).ToListAsync());
    }

    [Fact]
    public async Task The_backfilled_legacy_identity_key_prevents_registering_the_same_path_again()
    {
        await MigrateToPreRegistrationShapeAsync();
        await InsertLegacyProjectRowAsync(KnownLegacyCanonicalPath);
        await MigrateToLatestAsync();

        await using var context = CreateContext();
        var duplicate = Project.Register(Guid.NewGuid(), "Attempted duplicate", KnownLegacyCanonicalPath, DateTimeOffset.UtcNow);
        context.Projects.Add(duplicate);

        // The backfilled legacy row's RegistrationIdentityKey is the real ToUpperInvariant() of
        // its path, exactly what a fresh registration of the same path computes — the unique
        // index rejects it, proving the legacy row genuinely blocks re-registration rather than
        // silently permitting a duplicate under an unrelated placeholder key.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Migrating_fails_closed_when_an_unanticipated_legacy_project_row_exists()
    {
        await MigrateToPreRegistrationShapeAsync();
        await InsertLegacyProjectRowAsync(@"C:\repos\some-other-preexisting-project");

        // Never invents an identity key for a row this migration cannot verify — which could
        // permit a duplicate registration of the same path under a different key. Fails the
        // whole migration closed via a real NOT NULL constraint violation instead.
        var exception = await Record.ExceptionAsync(MigrateToLatestAsync);

        Assert.NotNull(exception);
    }
}
