using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

/// <summary>
/// The immutable, ordered history of the host-wide recorded-mutating admission limit (ADR-0059). Revision one, limit two, is seeded by the
/// migration for existing and new databases; the latest revision is the current setting. The checks also require the INTEGER storage class,
/// so a text, real or blob value is never stored as a setting. Nothing here cascades from or to another table.
/// </summary>
public sealed class ExecutionCapacityConfigurationRevisionConfiguration : IEntityTypeConfiguration<ExecutionCapacityConfigurationRevision>
{
    public void Configure(EntityTypeBuilder<ExecutionCapacityConfigurationRevision> builder)
    {
        builder.ToTable("execution_capacity_configurations", table =>
        {
            table.HasCheckConstraint("ck_execution_capacity_configurations_revision", "typeof(\"Revision\") = 'integer' AND \"Revision\" >= 1");
            table.HasCheckConstraint("ck_execution_capacity_configurations_limit", "typeof(\"Limit\") = 'integer' AND \"Limit\" IN (1, 2)");
        });
        builder.HasKey(configuration => configuration.Revision);
        builder.Property(configuration => configuration.Revision).ValueGeneratedNever();
        builder.Property(configuration => configuration.Limit).IsRequired();
        builder.HasData(new { Revision = 1L, Limit = ExecutionCapacityConfigurationRevision.DefaultLimit });
    }
}
