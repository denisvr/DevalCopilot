using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class AttemptConfiguration : IEntityTypeConfiguration<Attempt>
{
    public void Configure(EntityTypeBuilder<Attempt> builder)
    {
        builder.ToTable("attempts");
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasOne<Run>().WithMany().HasForeignKey(attempt => attempt.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(attempt => new { attempt.RunId, attempt.AttemptNumber }).IsUnique();
    }
}
