using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Infrastructure.Persistence;

public sealed class DevalCopilotDbContext(DbContextOptions<DevalCopilotDbContext> options)
    : DbContext(options), IDevalCopilotDbContext
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Run> Runs => Set<Run>();

    public DbSet<Attempt> Attempts => Set<Attempt>();

    public DbSet<RunEvent> Events => Set<RunEvent>();

    public DbSet<Artifact> Artifacts => Set<Artifact>();

    public DbSet<HostCapabilitySnapshot> HostCapabilitySnapshots => Set<HostCapabilitySnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DevalCopilotDbContext).Assembly);
    }
}
