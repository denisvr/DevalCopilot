using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Projects;

public sealed class CheckpointReviewEvidenceConfiguration : IEntityTypeConfiguration<CheckpointReviewEvidence>
{
    public void Configure(EntityTypeBuilder<CheckpointReviewEvidence> builder)
    {
        builder.ToTable("checkpoint_review_evidence");
        builder.HasKey(evidence => evidence.Id);
        builder.Property(evidence => evidence.VerificationExecutionNumber).IsRequired();
        builder.Property(evidence => evidence.VerificationExecutionCheckpointFingerprintSha256).HasMaxLength(64).IsRequired();
        builder.Property(evidence => evidence.VerificationExecutionStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(evidence => evidence.VerificationExecutionOutcome).HasConversion<string>().HasMaxLength(32);

        builder.HasOne<VerificationCommand>().WithMany().HasForeignKey(evidence => evidence.VerificationCommandId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VerificationExecution>().WithMany().HasForeignKey(evidence => evidence.VerificationExecutionId).OnDelete(DeleteBehavior.Restrict);

        // At most one evidence row per review per command, and never the same execution claimed
        // twice by the same review — Domain already enforces both; these are the durable
        // constraints backing that invariant.
        builder.HasIndex(evidence => new { evidence.CheckpointReviewId, evidence.VerificationCommandId }).IsUnique();
        builder.HasIndex(evidence => new { evidence.CheckpointReviewId, evidence.VerificationExecutionId }).IsUnique();
    }
}
