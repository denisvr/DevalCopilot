using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class GitHubBranchPublicationApprovalConfiguration : IEntityTypeConfiguration<GitHubBranchPublicationApproval>
{
    public void Configure(EntityTypeBuilder<GitHubBranchPublicationApproval> builder)
    {
        builder.ToTable("github_branch_publication_approvals");
        builder.HasKey(approval => approval.Id);
        builder.Property(approval => approval.CommitSha).HasMaxLength(40).IsRequired();
        builder.Property(approval => approval.BranchName).HasMaxLength(GitHubObservationTarget.MaximumBranchNameLength).IsRequired();
        builder.Property(approval => approval.TargetOwner).HasMaxLength(GitHubRepositoryNamePolicy.MaximumOwnerLength).IsRequired();
        builder.Property(approval => approval.TargetRepository).HasMaxLength(GitHubRepositoryNamePolicy.MaximumRepositoryLength).IsRequired();
        builder.Property(approval => approval.RepositoryNodeId).HasMaxLength(GitHubRepositoryNodeIdPolicy.MaximumLength).IsRequired();
        builder.Property(approval => approval.BindingOwner).HasMaxLength(GitHubRepositoryNamePolicy.MaximumOwnerLength).IsRequired();
        builder.Property(approval => approval.BindingRepository).HasMaxLength(GitHubRepositoryNamePolicy.MaximumRepositoryLength).IsRequired();

        // One immutable approval per run, per local-commit operation, per target, per binding and per Human event. The unique indexes are
        // the durable backstop of the write-locked transaction; the update and delete triggers of the migration make the row immutable.
        builder.HasIndex(approval => approval.RunId).IsUnique().HasDatabaseName("ix_github_branch_publication_approvals_run");
        builder.HasIndex(approval => approval.LocalCommitOperationId).IsUnique()
            .HasDatabaseName("ix_github_branch_publication_approvals_operation");
        builder.HasIndex(approval => approval.TargetId).IsUnique().HasDatabaseName("ix_github_branch_publication_approvals_target");
        builder.HasIndex(approval => approval.BindingId).IsUnique().HasDatabaseName("ix_github_branch_publication_approvals_binding");
        builder.HasIndex(approval => approval.EventId).IsUnique().HasDatabaseName("ix_github_branch_publication_approvals_event");

        // Every reference is restrictive: nothing cascades into this record, so no deletion can silently remove it or what it names.
        builder.HasOne<Run>().WithMany().HasForeignKey(approval => approval.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(approval => approval.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalCommitOperation>().WithMany().HasForeignKey(approval => approval.LocalCommitOperationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitWorkspace>().WithMany().HasForeignKey(approval => approval.GitWorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitHubObservationTarget>().WithMany().HasForeignKey(approval => approval.TargetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitHubRepositoryIdentityBinding>().WithMany().HasForeignKey(approval => approval.BindingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
