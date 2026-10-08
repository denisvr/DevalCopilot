using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class LocalCommitOperationConfiguration : IEntityTypeConfiguration<LocalCommitOperation>
{
    public void Configure(EntityTypeBuilder<LocalCommitOperation> builder)
    {
        builder.ToTable("local_commit_operations");
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.CheckpointFingerprintSha256).HasMaxLength(64).IsRequired();
        builder.Property(operation => operation.RequestSha256).HasMaxLength(64).IsRequired();
        builder.Property(operation => operation.AuthoritySha256).HasMaxLength(64).IsRequired();
        builder.Property(operation => operation.NormalizedMessage).HasMaxLength(2048).IsRequired();
        builder.Property(operation => operation.BranchName).HasMaxLength(512).IsRequired();
        builder.Property(operation => operation.ParentCommitSha).HasMaxLength(40).IsRequired();
        builder.Property(operation => operation.TreeSha).HasMaxLength(40).IsRequired();
        builder.Property(operation => operation.CommitSha).HasMaxLength(40).IsRequired();
        builder.Property(operation => operation.AuthorName).HasMaxLength(256).IsRequired();
        builder.Property(operation => operation.AuthorEmail).HasMaxLength(256).IsRequired();
        builder.Property(operation => operation.IndexPreimageSha256).HasMaxLength(64).IsRequired();
        builder.Property(operation => operation.PreparedIndexSha256).HasMaxLength(64).IsRequired();
        builder.Property(operation => operation.PreparedIndexRelativePath).HasMaxLength(512).IsRequired();
        builder.Property(operation => operation.IndexAdministrativeDirectoryIdentity).HasMaxLength(49);
        builder.Property(operation => operation.IndexPreimageIdentity).HasMaxLength(49);
        builder.Property(operation => operation.PreparedIndexArtifactIdentity).HasMaxLength(49);
        builder.Property(operation => operation.IndexLockIdentity).HasMaxLength(49);
        builder.Property(operation => operation.IndexQuarantineName).HasMaxLength(240);
        builder.Property(operation => operation.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(operation => operation.OutcomeReasonCode).HasMaxLength(128);
        builder.Ignore(operation => operation.CommitMessage);
        builder.Ignore(operation => operation.IsTerminal);

        // The status is a concurrency token so the supervisor and recovery can never both record an outcome for the same
        // operation: the second UPDATE finds a status it did not read and affects no row.
        builder.Property(operation => operation.Status).IsConcurrencyToken();

        // At most one operation is ever admitted per run, and at most one per workspace may be open at a time is enforced
        // by the Committing reservation. The run index is the durable backstop for the admission race.
        builder.HasIndex(operation => operation.RunId).IsUnique().HasDatabaseName("ix_local_commit_operations_run");
        builder.HasIndex(operation => operation.GitWorkspaceId).HasDatabaseName("ix_local_commit_operations_workspace");

        builder.HasMany(operation => operation.Members)
            .WithOne()
            .HasForeignKey(member => member.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(operation => operation.Members).HasField("members").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Run>().WithMany().HasForeignKey(operation => operation.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GitWorkspace>().WithMany().HasForeignKey(operation => operation.GitWorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitCheckpoint>().WithMany().HasForeignKey(operation => operation.GitCheckpointId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RepositoryMutationLease>().WithMany().HasForeignKey(operation => operation.RepositoryMutationLeaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(operation => operation.CodeReviewAttemptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CheckpointReview>().WithMany().HasForeignKey(operation => operation.HumanCheckpointReviewId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CheckpointReview>().WithMany().HasForeignKey(operation => operation.AgentCheckpointReviewId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
