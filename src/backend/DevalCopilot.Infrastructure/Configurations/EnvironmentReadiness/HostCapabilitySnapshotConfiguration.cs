using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.EnvironmentReadiness;

public sealed class HostCapabilitySnapshotConfiguration : IEntityTypeConfiguration<HostCapabilitySnapshot>
{
    public void Configure(EntityTypeBuilder<HostCapabilitySnapshot> builder)
    {
        builder.ToTable("host_capability_snapshots");

        // Capability itself is the natural key: exactly one row per fixed catalog capability,
        // ever, regardless of how many projects are registered.
        builder.HasKey(snapshot => snapshot.Capability);
        builder.Property(snapshot => snapshot.Capability).HasConversion<string>().HasMaxLength(32);

        builder.Property(snapshot => snapshot.ReasonCode).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(snapshot => snapshot.ResolvedExecutablePath).HasMaxLength(1024);
        builder.Property(snapshot => snapshot.ObservedVersion).HasMaxLength(128);
    }
}
