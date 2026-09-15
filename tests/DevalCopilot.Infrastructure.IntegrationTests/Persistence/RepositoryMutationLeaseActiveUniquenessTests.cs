using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves the <c>repository_mutation_leases</c> partial unique index directly against a real
/// SQLite database: at most one <see cref="LeaseStatus.Active"/> row may ever exist for a given
/// physical identity, but every <see cref="LeaseStatus.Released"/>/<see cref="LeaseStatus.Superseded"/>
/// row remains fully retained and queryable — never deleted to make exclusivity work.
/// </summary>
public sealed class RepositoryMutationLeaseActiveUniquenessTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-lease-uniqueness-{Guid.NewGuid():N}.db");

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

    private async Task<(Project Project, GitWorkspace WorkspaceA, GitWorkspace WorkspaceB)> SeedAsync(DevalCopilotDbContext dbContext)
    {
        var project = Project.Register(Guid.NewGuid(), "Test", @"C:\repos\lease-test", Now);
        var fileId = new byte[16];
        fileId[0] = 5;
        project.RecordPhysicalIdentityResolved(1UL, fileId);

        var workspaceA = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, project.ReserveWorkspaceNumber(), @"C:\workspaces\p\1",
            "devalcopilot/workspace/p/1", new string('a', 40), "main", Now);
        var workspaceB = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, project.ReserveWorkspaceNumber(), @"C:\workspaces\p\2",
            "devalcopilot/workspace/p/2", new string('b', 40), "main", Now);

        dbContext.Projects.Add(project);
        dbContext.GitWorkspaces.AddRange(workspaceA, workspaceB);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (project, workspaceA, workspaceB);
    }

    [Fact]
    public async Task A_second_active_lease_for_the_same_physical_identity_is_rejected_by_the_database()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        var (project, workspaceA, workspaceB) = await SeedAsync(dbContext);
        var fileId = project.PhysicalFileId!;

        dbContext.RepositoryMutationLeases.Add(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspaceA.Id, 1UL, fileId, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        dbContext.RepositoryMutationLeases.Add(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspaceB.Id, 1UL, fileId, Now));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_new_active_lease_is_allowed_once_the_first_is_released_and_both_remain_queryable()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        var (project, workspaceA, workspaceB) = await SeedAsync(dbContext);
        var fileId = project.PhysicalFileId!;

        var leaseA = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspaceA.Id, 1UL, fileId, Now);
        dbContext.RepositoryMutationLeases.Add(leaseA);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        leaseA.Release(Now.AddMinutes(1));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var leaseB = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspaceB.Id, 1UL, fileId, Now.AddMinutes(2));
        dbContext.RepositoryMutationLeases.Add(leaseB);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var allLeases = await dbContext.RepositoryMutationLeases
            .Where(l => l.PhysicalVolumeSerialNumber == 1UL)
            .ToListAsync();

        Assert.Equal(2, allLeases.Count);
        Assert.Contains(allLeases, l => l.Id == leaseA.Id && l.Status == LeaseStatus.Released);
        Assert.Contains(allLeases, l => l.Id == leaseB.Id && l.Status == LeaseStatus.Active);
    }
}
