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
        builder.Property(review => review.Decision).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(review => review.ActorKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(review => review.RecordedAtUtcTicks).HasColumnType("INTEGER").IsRequired();
        builder.HasOne<Project>().WithMany().HasForeignKey(review => review.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GitWorkspace>().WithMany().HasForeignKey(review => review.GitWorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitCheckpoint>().WithMany().HasForeignKey(review => review.GitCheckpointId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(review => new { review.ProjectId, review.RecordedAtUtcTicks, review.Id });

        // The relational evidence-membership set replaces the legacy single-execution snapshot
        // columns entirely — never retained alongside it. Mirrors GitCheckpoint.ChangedFiles.
        builder.HasMany(review => review.Evidence)
            .WithOne()
            .HasForeignKey(evidence => evidence.CheckpointReviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
