using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class ArtifactConfiguration : IEntityTypeConfiguration<Artifact>
{
    public void Configure(EntityTypeBuilder<Artifact> builder)
    {
        builder.ToTable("artifacts");
        builder.HasKey(artifact => artifact.Id);

        builder.Property(artifact => artifact.Purpose).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(artifact => artifact.CaptureOutcome).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(artifact => artifact.Sensitivity).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(artifact => artifact.RetentionPolicy).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(artifact => artifact.MediaType).HasMaxLength(128).IsRequired();
        builder.Property(artifact => artifact.RelativeStoragePath).HasMaxLength(1024).IsRequired();
        builder.Property(artifact => artifact.ContentHash).HasMaxLength(128).IsRequired();

        builder.HasOne<Attempt>().WithMany().HasForeignKey(artifact => artifact.AttemptId).OnDelete(DeleteBehavior.Cascade);

        // The recovery invariant: at most one durable artifact per (attempt, purpose), ever —
        // the primary defense is an application-level existence check before insert, and this
        // is the database backstop.
        builder.HasIndex(artifact => new { artifact.AttemptId, artifact.Purpose }).IsUnique();
    }
}
