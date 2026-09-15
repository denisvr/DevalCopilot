using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Projects;

public sealed class GitChangedFileConfiguration : IEntityTypeConfiguration<GitChangedFile>
{
    public void Configure(EntityTypeBuilder<GitChangedFile> builder)
    {
        builder.ToTable("git_changed_files");
        builder.HasKey(file => file.Id);
        builder.Property(file => file.Path).HasMaxLength(4096).IsRequired();
        builder.Property(file => file.PreviousPath).HasMaxLength(4096);
        builder.Property(file => file.IndexStatus).HasMaxLength(1).IsRequired();
        builder.Property(file => file.WorkTreeStatus).HasMaxLength(1).IsRequired();

        builder.HasIndex(file => file.CheckpointId);
    }
}
