using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class ReviewCorrectionEscalationConfiguration : IEntityTypeConfiguration<ReviewCorrectionEscalation>
{
    public void Configure(EntityTypeBuilder<ReviewCorrectionEscalation> builder)
    {
        builder.ToTable("review_correction_escalations");
        builder.HasKey(escalation => escalation.Id);
        builder.HasIndex(escalation => escalation.ImplementationReviewAttemptId).IsUnique();
        builder.HasOne<Run>().WithMany().HasForeignKey(escalation => escalation.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(escalation => escalation.ImplementationReviewAttemptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CollaborationMessage>().WithMany().HasForeignKey(escalation => escalation.CollaborationMessageId)
            .HasPrincipalKey(message => message.Id).OnDelete(DeleteBehavior.Restrict);
    }
}
