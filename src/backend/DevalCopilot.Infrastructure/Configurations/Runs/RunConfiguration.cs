using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class RunConfiguration : IEntityTypeConfiguration<Run>
{
    public void Configure(EntityTypeBuilder<Run> builder)
    {
        builder.ToTable("runs");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Objective).HasMaxLength(4000).IsRequired();
        builder.Property(run => run.Lifecycle).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(run => run.Stage).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(run => run.ActiveParticipantKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(run => run.ActiveAgentRole).HasConversion<string>().HasMaxLength(32);
        builder.Property(run => run.ActiveAgentProvider).HasConversion<string>().HasMaxLength(32);
        builder.Ignore(run => run.ActiveParticipant);
        builder.Property(run => run.AccumulatedAutonomousSeconds).IsRequired();

        builder.HasOne<Project>().WithMany().HasForeignKey(run => run.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(run => new { run.ProjectId, run.ExecutionNumber }).IsUnique();
    }
}
