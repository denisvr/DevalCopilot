using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class LocalExecutionSequenceRecipeConfiguration : IEntityTypeConfiguration<LocalExecutionSequenceRecipe>
{
    public void Configure(EntityTypeBuilder<LocalExecutionSequenceRecipe> builder)
    {
        builder.ToTable("local_execution_sequence_recipes", table =>
        {
            table.HasCheckConstraint("ck_local_execution_sequence_recipes_ordinal", "\"Ordinal\" BETWEEN 0 AND 31");
            table.HasCheckConstraint("ck_local_execution_sequence_recipes_timeout", "\"TimeoutSeconds\" BETWEEN 1 AND 900");
            table.HasCheckConstraint("ck_local_execution_sequence_recipes_enabled", "\"IsEnabled\" = 1");
        });
        builder.HasKey(recipe => recipe.Id);

        builder.Property(recipe => recipe.CommandName).HasMaxLength(200).IsRequired();
        builder.Property(recipe => recipe.ExecutablePath).HasMaxLength(1024).IsRequired();
        builder.Property(recipe => recipe.ArgumentsJson).IsRequired();

        // The membership is a relational fact: unique positions and unique recipe identities within the sequence.
        builder.HasIndex(recipe => new { recipe.SequenceId, recipe.Ordinal }).IsUnique().HasDatabaseName("ix_local_execution_sequence_recipes_ordinal");
        builder.HasIndex(recipe => new { recipe.SequenceId, recipe.VerificationCommandId })
            .IsUnique()
            .HasDatabaseName("ix_local_execution_sequence_recipes_command");
        builder.HasIndex(recipe => new { recipe.SequenceId, recipe.CommandNumber })
            .IsUnique()
            .HasDatabaseName("ix_local_execution_sequence_recipes_number");

        // The recipe is pinned by value: no foreign key to the project's recipe, so a later change or deletion is never blocked by it.
        builder.HasOne<LocalExecutionSequence>().WithMany().HasForeignKey(recipe => recipe.SequenceId).OnDelete(DeleteBehavior.Cascade);
    }
}
