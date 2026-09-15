using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Projects;

public sealed class GitWorkspaceConfiguration : IEntityTypeConfiguration<GitWorkspace>
{
    public void Configure(EntityTypeBuilder<GitWorkspace> builder)
    {
        builder.ToTable("git_workspaces");
        builder.HasKey(workspace => workspace.Id);
        builder.Property(workspace => workspace.WorkspaceNumber).IsRequired();
        builder.Property(workspace => workspace.NextCheckpointNumber).HasDefaultValue(1).IsRequired();
        builder.Property(workspace => workspace.WorkspacePath).HasMaxLength(1000).IsRequired();
        builder.Property(workspace => workspace.BranchName).HasMaxLength(512).IsRequired();
        builder.Property(workspace => workspace.SourceCommitSha).HasMaxLength(40).IsRequired();
        builder.Property(workspace => workspace.SourceBranchName).HasMaxLength(512);
        builder.Property(workspace => workspace.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(workspace => workspace.CreatedAtUtc).IsRequired();
        builder.Property(workspace => workspace.LastFailureReasonCode).HasMaxLength(128);

        builder.HasOne<Project>().WithMany().HasForeignKey(workspace => workspace.ProjectId).OnDelete(DeleteBehavior.Cascade);

        // Deterministic ordering backstop, mirroring RepositoryBaselineConfiguration: "current"
        // is the greatest WorkspaceNumber for a project, never a timestamp — this index also
        // guarantees ReserveWorkspaceNumber() can never be bypassed into producing two rows
        // with the same number.
        builder.HasIndex(workspace => new { workspace.ProjectId, workspace.WorkspaceNumber }).IsUnique();
    }
}
