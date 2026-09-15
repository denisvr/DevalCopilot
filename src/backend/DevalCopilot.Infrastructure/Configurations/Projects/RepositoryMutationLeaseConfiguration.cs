using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Projects;

public sealed class RepositoryMutationLeaseConfiguration : IEntityTypeConfiguration<RepositoryMutationLease>
{
    public void Configure(EntityTypeBuilder<RepositoryMutationLease> builder)
    {
        builder.ToTable("repository_mutation_leases");
        builder.HasKey(lease => lease.Id);
        builder.Property(lease => lease.PhysicalVolumeSerialNumber).IsRequired();
        builder.Property(lease => lease.PhysicalFileId).HasMaxLength(16).IsRequired();
        builder.Property(lease => lease.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(lease => lease.AcquiredAtUtc).IsRequired();
        builder.Property(lease => lease.ReleasedAtUtc);
        builder.Property(lease => lease.SupersededAtUtc);

        builder.HasOne<Project>().WithMany().HasForeignKey(lease => lease.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GitWorkspace>().WithMany().HasForeignKey(lease => lease.WorkspaceId).OnDelete(DeleteBehavior.Cascade);

        // A lease is created 1:1 with its workspace and never re-pointed at another one.
        builder.HasIndex(lease => lease.WorkspaceId).IsUnique();

        // The actual mutual-exclusion enforcement: at most one Active lease may ever exist for
        // a given physical repository, but Released/Superseded rows remain fully retained and
        // queryable — a plain unique index would incorrectly forbid every lease after the
        // first release. SQLite (>= 3.8.0) supports partial/filtered unique indexes; EF Core's
        // SQLite provider generates the WHERE clause from HasFilter verbatim. See ADR-0008.
        builder.HasIndex(lease => new { lease.PhysicalVolumeSerialNumber, lease.PhysicalFileId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'")
            .HasDatabaseName("IX_repository_mutation_leases_physical_identity_active");
    }
}
