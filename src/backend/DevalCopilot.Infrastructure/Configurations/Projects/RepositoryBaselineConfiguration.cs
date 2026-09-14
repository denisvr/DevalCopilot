using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Projects;

public sealed class RepositoryBaselineConfiguration : IEntityTypeConfiguration<RepositoryBaseline>
{
    public void Configure(EntityTypeBuilder<RepositoryBaseline> builder)
    {
        builder.ToTable("repository_baselines");
        builder.HasKey(baseline => baseline.Id);
        builder.Property(baseline => baseline.BaselineNumber).IsRequired();
        builder.Property(baseline => baseline.ObservedAtUtc).IsRequired();
        builder.Property(baseline => baseline.HeadState).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(baseline => baseline.BranchName).HasMaxLength(512);
        builder.Property(baseline => baseline.HeadCommitSha).HasMaxLength(40);
        builder.Property(baseline => baseline.IsDirty).IsRequired();

        builder.HasOne<Project>().WithMany().HasForeignKey(baseline => baseline.ProjectId).OnDelete(DeleteBehavior.Cascade);

        // Deterministic ordering backstop: "current" is the greatest BaselineNumber for a
        // project, never ObservedAtUtc — this index also guarantees ReserveBaselineNumber()
        // can never be bypassed into producing two rows with the same number.
        builder.HasIndex(baseline => new { baseline.ProjectId, baseline.BaselineNumber }).IsUnique();
    }
}
