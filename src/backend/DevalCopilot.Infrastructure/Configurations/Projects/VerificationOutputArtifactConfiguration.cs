using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Projects;

public sealed class VerificationOutputArtifactConfiguration : IEntityTypeConfiguration<VerificationOutputArtifact>
{
    public void Configure(EntityTypeBuilder<VerificationOutputArtifact> builder)
    {
        builder.ToTable("verification_output_artifacts");
        builder.HasKey(artifact => artifact.Id);
        builder.Property(artifact => artifact.Purpose).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(artifact => artifact.RelativeStoragePath).HasMaxLength(1024).IsRequired();
        builder.Property(artifact => artifact.ContentHash).HasMaxLength(128).IsRequired();
        builder.Property(artifact => artifact.CaptureOutcome).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.HasOne<VerificationExecution>().WithMany().HasForeignKey(artifact => artifact.VerificationExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(artifact => new { artifact.VerificationExecutionId, artifact.Purpose }).IsUnique();
    }
}
