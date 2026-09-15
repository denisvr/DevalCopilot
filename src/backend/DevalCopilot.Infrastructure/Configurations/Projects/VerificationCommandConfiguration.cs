using System.Text.Json;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Projects;

public sealed class VerificationCommandConfiguration : IEntityTypeConfiguration<VerificationCommand>
{
    private static readonly ValueComparer<IReadOnlyList<string>> ArgumentsComparer = new(
        (left, right) => (left ?? Array.Empty<string>()).SequenceEqual(right ?? Array.Empty<string>()),
        value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
        value => value.ToArray());

    public void Configure(EntityTypeBuilder<VerificationCommand> builder)
    {
        builder.ToTable("verification_commands");
        builder.HasKey(command => command.Id);
        builder.Property(command => command.CommandNumber).IsRequired();
        builder.Property(command => command.Name).HasMaxLength(200).IsRequired();
        builder.Property(command => command.ExecutablePath).HasMaxLength(1024).IsRequired();
        builder.Property(command => command.TimeoutSeconds).IsRequired();
        builder.Property(command => command.IsEnabled).IsRequired();
        builder.Property(command => command.ConfiguredAtUtc).IsRequired();
        builder.Property(command => command.UpdatedAtUtc).IsRequired();
        builder.Property(command => command.Arguments)
            .HasConversion(
                arguments => arguments.Count == 0 ? string.Empty : JsonSerializer.Serialize(arguments, (JsonSerializerOptions?)null),
                json => string.IsNullOrEmpty(json)
                    ? Array.Empty<string>()
                    : JsonSerializer.Deserialize<string[]>(json, (JsonSerializerOptions?)null)!)
            .IsRequired()
            .Metadata.SetValueComparer(ArgumentsComparer);

        builder.HasOne<Project>().WithMany().HasForeignKey(command => command.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(command => new { command.ProjectId, command.CommandNumber }).IsUnique();
    }
}
