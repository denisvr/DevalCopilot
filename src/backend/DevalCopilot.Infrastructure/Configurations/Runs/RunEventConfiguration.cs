using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class RunEventConfiguration : IEntityTypeConfiguration<RunEvent>
{
    public void Configure(EntityTypeBuilder<RunEvent> builder)
    {
        builder.ToTable("events");

        // The monotonic sequence is the real ordering key and SQLite rowid, distinct
        // from the durable Id used to reference one event from elsewhere.
        builder.HasKey(runEvent => runEvent.Sequence);
        builder.Property(runEvent => runEvent.Sequence).ValueGeneratedOnAdd();
        builder.HasIndex(runEvent => runEvent.Id).IsUnique();

        builder.Property(runEvent => runEvent.EventType).HasMaxLength(100).IsRequired();
        builder.Property(runEvent => runEvent.Actor).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(runEvent => runEvent.PayloadJson).IsRequired();

        builder.HasOne<Run>().WithMany().HasForeignKey(runEvent => runEvent.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(runEvent => runEvent.AttemptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(runEvent => new { runEvent.RunId, runEvent.Sequence });
    }
}
