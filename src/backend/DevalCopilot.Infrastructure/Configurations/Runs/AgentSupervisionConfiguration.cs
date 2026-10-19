using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class AgentSupervisionConfiguration : IEntityTypeConfiguration<AgentSupervision>
{
    private const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    public void Configure(EntityTypeBuilder<AgentSupervision> builder)
    {
        builder.ToTable("agent_supervisions", table =>
        {
            table.HasCheckConstraint("ck_agent_supervisions_revision", "\"Revision\" >= 1");
            table.HasCheckConstraint(
                "ck_agent_supervisions_phase", "\"Phase\" IN ('Preparing', 'AwaitingAdapterResult', 'RecordingResult')");
            table.HasCheckConstraint(
                "ck_agent_supervisions_identity",
                $"\"Id\" <> {EmptyGuid} AND \"RunId\" <> {EmptyGuid} AND \"AttemptId\" <> {EmptyGuid} "
                + $"AND \"HostInstanceId\" <> {EmptyGuid} AND \"ObserverId\" <> {EmptyGuid}");
            table.HasCheckConstraint(
                "ck_agent_supervisions_attention",
                "(\"AttentionReason\" IS NULL) = (\"AttentionAtUtc\" IS NULL) AND (\"AttentionReason\" IS NULL OR \"AttentionReason\" IN "
                + "('stale_heartbeat', 'phase_deadline_elapsed', 'observer_ended_without_terminal_result', 'host_interrupted'))");
            table.HasCheckConstraint(
                "ck_agent_supervisions_closure",
                "(\"ClosureReason\" IS NULL) = (\"ClosedAtUtc\" IS NULL) AND (\"ClosureReason\" IS NULL OR \"ClosureReason\" IN "
                + "('attempt_terminal', 'observer_ended', 'host_interrupted'))");
        });
        builder.HasKey(supervision => supervision.Id);

        builder.Property(supervision => supervision.Phase).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(supervision => supervision.AttentionReason).HasMaxLength(64);
        builder.Property(supervision => supervision.ClosureReason).HasMaxLength(64);
        builder.Ignore(supervision => supervision.IsOpen);

        // Every transition compares the revision it read: two writers can never both apply a decision made from the same read.
        builder.Property(supervision => supervision.Revision).IsConcurrencyToken();

        // One observation per observed Agent attempt for the attempt's whole lifetime: a competing identity never replaces or adopts it.
        builder.HasIndex(supervision => supervision.AttemptId).IsUnique().HasDatabaseName("ix_agent_supervisions_attempt");
        builder.HasIndex(supervision => supervision.RunId).HasDatabaseName("ix_agent_supervisions_run");
        builder.HasIndex(supervision => supervision.HostInstanceId)
            .HasFilter("\"ClosedAtUtc\" IS NULL")
            .HasDatabaseName("ix_agent_supervisions_open");

        // Retained for the run's lifetime: no cascade, so a future run deletion must explicitly own the hard deletion of these rows.
        builder.HasOne<Run>().WithMany().HasForeignKey(supervision => supervision.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(supervision => supervision.AttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
