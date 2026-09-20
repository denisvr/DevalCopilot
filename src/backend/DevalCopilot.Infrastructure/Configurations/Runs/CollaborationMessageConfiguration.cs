using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class CollaborationMessageConfiguration : IEntityTypeConfiguration<CollaborationMessage>
{
    public void Configure(EntityTypeBuilder<CollaborationMessage> builder)
    {
        builder.ToTable("collaboration_messages");
        builder.HasKey(message => message.Sequence);
        builder.Property(message => message.Sequence).ValueGeneratedOnAdd();
        builder.HasIndex(message => message.Id).IsUnique();
        builder.HasIndex(message => new { message.RunId, message.Sequence }).IsUnique();
        builder.HasIndex(message => new { message.RunId, message.InReplyToMessageId });

        builder.Property(message => message.ProtocolVersion).HasMaxLength(8).IsRequired();
        builder.Property(message => message.ActorKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(message => message.ActorAgentRole).HasConversion<string>().HasMaxLength(32);
        builder.Property(message => message.ActorAgentProvider).HasConversion<string>().HasMaxLength(32);
        builder.Property(message => message.RecipientKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(message => message.RecipientAgentRole).HasConversion<string>().HasMaxLength(32);
        builder.Property(message => message.RecipientAgentProvider).HasConversion<string>().HasMaxLength(32);
        builder.Ignore(message => message.Actor);
        builder.Ignore(message => message.Recipient);
        builder.Property(message => message.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(message => message.Provenance).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(message => message.Summary).HasMaxLength(CollaborationMessageContentPolicy.MaximumSummaryLength).IsRequired();
        builder.Property(message => message.StructuredContentJson)
            .HasMaxLength(CollaborationMessageContentPolicy.MaximumStructuredContentLength)
            .IsRequired();

        builder.HasOne<Run>().WithMany().HasForeignKey(message => message.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(message => message.AttemptId).OnDelete(DeleteBehavior.Cascade);
    }
}
