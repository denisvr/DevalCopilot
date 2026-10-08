using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class LocalCommitAuthorityMemberConfiguration : IEntityTypeConfiguration<LocalCommitAuthorityMember>
{
    public void Configure(EntityTypeBuilder<LocalCommitAuthorityMember> builder)
    {
        builder.ToTable("local_commit_authority_members");
        builder.HasKey(member => member.Id);
        builder.Property(member => member.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(member => member.Digest).HasMaxLength(64).IsRequired();
        builder.HasIndex(member => new { member.OperationId, member.Kind, member.Sequence })
            .IsUnique()
            .HasDatabaseName("ix_local_commit_authority_members_position");
    }
}
