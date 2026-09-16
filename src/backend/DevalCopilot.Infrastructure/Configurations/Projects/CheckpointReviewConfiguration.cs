using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Projects;

public sealed class CheckpointReviewConfiguration : IEntityTypeConfiguration<CheckpointReview>
{
    public void Configure(EntityTypeBuilder<CheckpointReview> builder)
    {
        builder.ToTable("checkpoint_reviews");
        builder.HasKey(review => review.Id);
        builder.Property(review => review.CheckpointFingerprintSha256).HasMaxLength(64).IsRequired();
        builder.Property(review => review.VerificationExecutionCheckpointFingerprintSha256).HasMaxLength(64);
        builder.Property(review => review.Decision).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(review => review.ActorKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(review => review.VerificationExecutionStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(review => review.VerificationExecutionOutcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(review => review.RecordedAtUtcTicks).HasColumnType("INTEGER").IsRequired();
        builder.HasOne<Project>().WithMany().HasForeignKey(review => review.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GitWorkspace>().WithMany().HasForeignKey(review => review.GitWorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitCheckpoint>().WithMany().HasForeignKey(review => review.GitCheckpointId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VerificationExecution>().WithMany().HasForeignKey(review => review.VerificationExecutionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(review => new { review.ProjectId, review.RecordedAtUtcTicks, review.Id });
        builder.HasIndex(review => new { review.GitCheckpointId, review.VerificationExecutionId });
    }
}
