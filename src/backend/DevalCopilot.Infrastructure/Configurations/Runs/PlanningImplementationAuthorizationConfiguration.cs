using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class PlanningImplementationAuthorizationConfiguration : IEntityTypeConfiguration<PlanningImplementationAuthorization>
{
    public void Configure(EntityTypeBuilder<PlanningImplementationAuthorization> builder)
    {
        builder.ToTable("planning_implementation_authorizations");
        builder.HasKey(authorization => authorization.Id);
        builder.Property(authorization => authorization.FingerprintSha256).HasMaxLength(64).IsRequired();

        // The consumed-by link is the one authoritative consumption location: a tracked consume UPDATE requires the
        // value it read (null) to still be stored, so two claims can never both spend the grant.
        builder.Property(authorization => authorization.ConsumedByAttemptId).IsConcurrencyToken();

        // At most one grant per escalation and per final Proposal; one instruction message belongs to one grant; one
        // attempt consumes at most one grant. No renewal, revocation or second grant exists.
        builder.HasIndex(authorization => authorization.EscalationMessageId)
            .IsUnique()
            .HasDatabaseName("ix_planning_implementation_authorizations_escalation");
        builder.HasIndex(authorization => authorization.FinalProposalMessageId)
            .IsUnique()
            .HasDatabaseName("ix_planning_implementation_authorizations_final_proposal");
        builder.HasIndex(authorization => authorization.HumanInstructionMessageId)
            .IsUnique()
            .HasDatabaseName("ix_planning_implementation_authorizations_instruction");
        builder.HasIndex(authorization => authorization.ConsumedByAttemptId)
            .IsUnique()
            .HasDatabaseName("ix_planning_implementation_authorizations_attempt_consumed")
            .HasFilter("\"ConsumedByAttemptId\" IS NOT NULL");
        builder.HasIndex(authorization => authorization.RunId);

        builder.HasOne<Run>().WithMany().HasForeignKey(authorization => authorization.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CollaborationMessage>().WithMany().HasForeignKey(authorization => authorization.EscalationMessageId)
            .HasPrincipalKey(message => message.Id).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CollaborationMessage>().WithMany().HasForeignKey(authorization => authorization.FinalProposalMessageId)
            .HasPrincipalKey(message => message.Id).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CollaborationMessage>().WithMany().HasForeignKey(authorization => authorization.HumanInstructionMessageId)
            .HasPrincipalKey(message => message.Id).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(authorization => authorization.ConsumedByAttemptId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
