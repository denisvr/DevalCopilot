using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Projects;

public sealed class GitCheckpointConfiguration : IEntityTypeConfiguration<GitCheckpoint>
{
    public void Configure(EntityTypeBuilder<GitCheckpoint> builder)
    {
        builder.ToTable("git_checkpoints");
        builder.HasKey(checkpoint => checkpoint.Id);
        builder.Property(checkpoint => checkpoint.CheckpointNumber).IsRequired();
        builder.Property(checkpoint => checkpoint.CapturedAtUtc).IsRequired();
        builder.Property(checkpoint => checkpoint.HeadCommitSha).HasMaxLength(40).IsRequired();
        builder.Property(checkpoint => checkpoint.FingerprintSha256).HasMaxLength(64).IsRequired();

        builder.HasOne<GitWorkspace>().WithMany().HasForeignKey(checkpoint => checkpoint.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(checkpoint => checkpoint.ChangedFiles).WithOne().HasForeignKey(file => file.CheckpointId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(checkpoint => new { checkpoint.WorkspaceId, checkpoint.CheckpointNumber }).IsUnique();
    }
}
