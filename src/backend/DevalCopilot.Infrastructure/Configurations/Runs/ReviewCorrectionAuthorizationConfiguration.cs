using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class ReviewCorrectionAuthorizationConfiguration : IEntityTypeConfiguration<ReviewCorrectionAuthorization>
{
    public void Configure(EntityTypeBuilder<ReviewCorrectionAuthorization> builder)
    {
        builder.ToTable("review_correction_authorizations");
        builder.HasKey(authorization => authorization.Id);
        builder.Property(authorization => authorization.ConsumedByAttemptId).IsConcurrencyToken();
        builder.HasIndex(authorization => authorization.EscalationId)
            .IsUnique()
            .HasDatabaseName("ix_review_correction_authorizations_one_available")
            .HasFilter("\"ConsumedByAttemptId\" IS NULL");
        builder.HasIndex(authorization => authorization.ConsumedByAttemptId)
            .IsUnique()
            .HasDatabaseName("ix_review_correction_authorizations_attempt_consumed")
            .HasFilter("\"ConsumedByAttemptId\" IS NOT NULL");
        builder.HasOne<Run>().WithMany().HasForeignKey(authorization => authorization.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ReviewCorrectionEscalation>().WithMany().HasForeignKey(authorization => authorization.EscalationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CollaborationMessage>().WithMany().HasForeignKey(authorization => authorization.HumanInstructionMessageId)
            .HasPrincipalKey(message => message.Id).OnDelete(DeleteBehavior.Restrict);
    }
}
