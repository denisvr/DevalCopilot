using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class GitHubRepositoryIdentityBindingConfiguration : IEntityTypeConfiguration<GitHubRepositoryIdentityBinding>
{
    public void Configure(EntityTypeBuilder<GitHubRepositoryIdentityBinding> builder)
    {
        builder.ToTable("github_repository_identity_bindings");
        builder.HasKey(binding => binding.Id);
        builder.Property(binding => binding.CommitSha).HasMaxLength(40).IsRequired();
        builder.Property(binding => binding.BranchName).HasMaxLength(GitHubObservationTarget.MaximumBranchNameLength).IsRequired();
        builder.Property(binding => binding.RepositoryNodeId).HasMaxLength(GitHubRepositoryNodeIdPolicy.MaximumLength).IsRequired();
        builder.Property(binding => binding.Owner).HasMaxLength(GitHubRepositoryNamePolicy.MaximumOwnerLength).IsRequired();
        builder.Property(binding => binding.Repository).HasMaxLength(GitHubRepositoryNamePolicy.MaximumRepositoryLength).IsRequired();

        // One immutable binding per target, per run, per local-commit operation and per Human event. A node identifier is deliberately not
        // unique across bindings. The unique indexes are the durable backstop of the write-locked transaction; there is no update,
        // replacement or delete path.
        builder.HasIndex(binding => binding.ObservationTargetId).IsUnique().HasDatabaseName("ix_github_repository_identity_bindings_target");
        builder.HasIndex(binding => binding.RunId).IsUnique().HasDatabaseName("ix_github_repository_identity_bindings_run");
        builder.HasIndex(binding => binding.LocalCommitOperationId).IsUnique()
            .HasDatabaseName("ix_github_repository_identity_bindings_operation");
        builder.HasIndex(binding => binding.EventId).IsUnique().HasDatabaseName("ix_github_repository_identity_bindings_event");

        // Every reference is restrictive: nothing cascades into this record, so no deletion can silently remove it or its target.
        builder.HasOne<GitHubObservationTarget>().WithMany().HasForeignKey(binding => binding.ObservationTargetId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Run>().WithMany().HasForeignKey(binding => binding.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(binding => binding.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalCommitOperation>().WithMany().HasForeignKey(binding => binding.LocalCommitOperationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitWorkspace>().WithMany().HasForeignKey(binding => binding.GitWorkspaceId).OnDelete(DeleteBehavior.Restrict);
    }
}
