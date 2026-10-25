using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

/// <summary>ADR-0065: the append-only physical termination observation. Durations are persisted as their exact nonnegative tick counts,
/// exit codes as exact 32-bit integers, and the semantic exit code exists exactly for Exited and equals the physical one. One observation
/// per witness. The migration adds the guards that refuse any update or delete.</summary>
public sealed class LocalExecutionTerminationObservationConfiguration : IEntityTypeConfiguration<LocalExecutionTerminationObservation>
{
    public void Configure(EntityTypeBuilder<LocalExecutionTerminationObservation> builder)
    {
        builder.ToTable("local_execution_termination_observations", table =>
        {
            table.HasCheckConstraint("ck_local_execution_termination_observations_version", "typeof(\"Version\") = 'integer' AND \"Version\" = 1");
            table.HasCheckConstraint(
                "ck_local_execution_termination_observations_outcome", "\"ProcessOutcome\" IN ('Exited', 'TimedOut', 'Cancelled')");
            table.HasCheckConstraint(
                "ck_local_execution_termination_observations_exit_codes",
                "typeof(\"PhysicalExitCode\") = 'integer' AND \"PhysicalExitCode\" BETWEEN -2147483648 AND 2147483647 "
                + "AND ((\"ProcessOutcome\" = 'Exited') = (\"SemanticExitCode\" IS NOT NULL)) "
                + "AND (\"SemanticExitCode\" IS NULL OR (typeof(\"SemanticExitCode\") = 'integer' AND \"SemanticExitCode\" = \"PhysicalExitCode\"))");
            table.HasCheckConstraint(
                "ck_local_execution_termination_observations_durations",
                "typeof(\"ProcessDuration\") = 'integer' AND typeof(\"ObservedElapsed\") = 'integer' AND \"ObservedElapsed\" >= 0 "
                + "AND \"ObservedElapsed\" <= \"ProcessDuration\"");
            table.HasCheckConstraint(
                "ck_local_execution_termination_observations_time", "typeof(\"RecordedAtUtc\") = 'text'");
        });
        builder.HasKey(observation => observation.Id);

        builder.Property(observation => observation.ProcessOutcome).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(observation => observation.ProcessDuration).HasConversion(duration => duration.Ticks, ticks => TimeSpan.FromTicks(ticks));
        builder.Property(observation => observation.ObservedElapsed).HasConversion(duration => duration.Ticks, ticks => TimeSpan.FromTicks(ticks));

        builder.HasIndex(observation => observation.WitnessId).IsUnique().HasDatabaseName("ix_local_execution_termination_observations_witness");

        builder.HasOne<LocalExecutionDispatchWitness>().WithMany().HasForeignKey(observation => observation.WitnessId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
