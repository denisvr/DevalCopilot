using System.Text.Json;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Projects;

public sealed class VerificationExecutionConfiguration : IEntityTypeConfiguration<VerificationExecution>
{
    private static readonly ValueComparer<IReadOnlyList<string>> ArgumentsComparer = new(
        (left, right) => (left ?? Array.Empty<string>()).SequenceEqual(right ?? Array.Empty<string>()),
        value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
        value => value.ToArray());

    public void Configure(EntityTypeBuilder<VerificationExecution> builder)
    {
        builder.ToTable("verification_executions");
        builder.HasKey(execution => execution.Id);
        builder.Property(execution => execution.ExecutionNumber).IsRequired();
        builder.Property(execution => execution.WorkspacePath).HasMaxLength(1024).IsRequired();
        builder.Property(execution => execution.CheckpointFingerprintSha256).HasMaxLength(64).IsRequired();
        builder.Property(execution => execution.CommandName).HasMaxLength(200).IsRequired();
        builder.Property(execution => execution.ExecutablePath).HasMaxLength(1024).IsRequired();
        builder.Property(execution => execution.TimeoutSeconds).IsRequired();
        builder.Property(execution => execution.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(execution => execution.Outcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(execution => execution.CompletionFingerprintSha256).HasMaxLength(64);
        builder.Property(execution => execution.Arguments)
            .HasConversion(
                arguments => arguments.Count == 0 ? string.Empty : JsonSerializer.Serialize(arguments, (JsonSerializerOptions?)null),
                json => string.IsNullOrEmpty(json)
                    ? Array.Empty<string>()
                    : JsonSerializer.Deserialize<string[]>(json, (JsonSerializerOptions?)null)!)
            .IsRequired()
            .Metadata.SetValueComparer(ArgumentsComparer);

        builder.HasOne<Project>().WithMany().HasForeignKey(execution => execution.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GitWorkspace>().WithMany().HasForeignKey(execution => execution.GitWorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GitCheckpoint>().WithMany().HasForeignKey(execution => execution.GitCheckpointId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VerificationCommand>().WithMany().HasForeignKey(execution => execution.VerificationCommandId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(execution => new { execution.ProjectId, execution.ExecutionNumber }).IsUnique();
        builder.HasIndex(execution => new { execution.GitWorkspaceId, execution.Status });
    }
}
