using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class GitHubObservationTargetConfiguration : IEntityTypeConfiguration<GitHubObservationTarget>
{
    public void Configure(EntityTypeBuilder<GitHubObservationTarget> builder)
    {
        builder.ToTable("github_observation_targets");
        builder.HasKey(target => target.Id);
        builder.Property(target => target.CommitSha).HasMaxLength(40).IsRequired();
        builder.Property(target => target.BranchName).HasMaxLength(GitHubObservationTarget.MaximumBranchNameLength).IsRequired();
        builder.Property(target => target.Owner).HasMaxLength(GitHubRepositoryNamePolicy.MaximumOwnerLength).IsRequired();
        builder.Property(target => target.Repository).HasMaxLength(GitHubRepositoryNamePolicy.MaximumRepositoryLength).IsRequired();

        // One immutable selection per run, per local-commit operation and per Human event. The unique indexes are the durable
        // backstop of the write-locked transaction; there is no update, replacement or delete path.
        builder.HasIndex(target => target.RunId).IsUnique().HasDatabaseName("ix_github_observation_targets_run");
        builder.HasIndex(target => target.LocalCommitOperationId).IsUnique().HasDatabaseName("ix_github_observation_targets_operation");
        builder.HasIndex(target => target.EventId).IsUnique().HasDatabaseName("ix_github_observation_targets_event");

        // Every reference is restrictive: nothing cascades into this record, so no deletion can silently remove it or its event.
        builder.HasOne<Run>().WithMany().HasForeignKey(target => target.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(target => target.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalCommitOperation>().WithMany().HasForeignKey(target => target.LocalCommitOperationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitWorkspace>().WithMany().HasForeignKey(target => target.GitWorkspaceId).OnDelete(DeleteBehavior.Restrict);
    }
}
