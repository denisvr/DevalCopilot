using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class AttemptInputMessageConfiguration : IEntityTypeConfiguration<AttemptInputMessage>
{
    public void Configure(EntityTypeBuilder<AttemptInputMessage> builder)
    {
        builder.ToTable("attempt_input_messages");
        builder.HasKey(inputMessage => inputMessage.Id);

        builder.HasOne<Attempt>().WithMany().HasForeignKey(inputMessage => inputMessage.AttemptId).OnDelete(DeleteBehavior.Cascade);

        // Never a navigation to CollaborationMessage: its own primary key is the monotonic
        // Sequence, not Id, so this column stays a bare, unconstrained identifier exactly like
        // every other cross-feature reference in this codebase (e.g. Attempt's own
        // AgentGitWorkspaceId/AgentGitCheckpointId).
        builder.Property(inputMessage => inputMessage.CollaborationMessageId).IsRequired();

        // The complete-input-set invariant's database backstop: no attempt may record the same
        // ordered position twice, and no attempt may record the same input message twice — the
        // primary defense is Application-level validation before insert, and these are the
        // database backstops.
        builder.HasIndex(inputMessage => new { inputMessage.AttemptId, inputMessage.Sequence }).IsUnique();
        builder.HasIndex(inputMessage => new { inputMessage.AttemptId, inputMessage.CollaborationMessageId }).IsUnique();
    }
}
