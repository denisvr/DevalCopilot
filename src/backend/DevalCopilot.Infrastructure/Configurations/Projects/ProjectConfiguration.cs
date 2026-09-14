using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Projects;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(project => project.Id);
        builder.Property(project => project.Name).HasMaxLength(200).IsRequired();
        builder.Property(project => project.CanonicalPath).HasMaxLength(1000).IsRequired();
        // Registration-only duplicate-detection key — never authoritative for repository
        // mutation exclusion. See Project.RegistrationIdentityKey's doc comment and ADR-0007.
        builder.Property(project => project.RegistrationIdentityKey).HasMaxLength(1000).IsRequired();
        // Nullable: null represents a legacy project honestly (its registration time is
        // genuinely unknown), never a fabricated historical date. Every project registered
        // through Project.Register always supplies a real value.
        builder.Property(project => project.RegisteredAtUtc);
        builder.Property(project => project.NextExecutionNumber).IsRequired();
        builder.Property(project => project.NextBaselineNumber).IsRequired();
        builder.HasIndex(project => project.RegistrationIdentityKey).IsUnique();
    }
}
