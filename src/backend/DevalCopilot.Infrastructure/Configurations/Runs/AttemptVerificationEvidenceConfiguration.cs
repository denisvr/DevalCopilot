using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class AttemptVerificationEvidenceConfiguration : IEntityTypeConfiguration<AttemptVerificationEvidence>
{
    public void Configure(EntityTypeBuilder<AttemptVerificationEvidence> builder)
    {
        builder.ToTable("attempt_verification_evidence");
        builder.HasKey(evidence => evidence.Id);

        builder.HasOne<Attempt>().WithMany().HasForeignKey(evidence => evidence.AttemptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<VerificationCommand>().WithMany().HasForeignKey(evidence => evidence.VerificationCommandId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VerificationExecution>().WithMany().HasForeignKey(evidence => evidence.VerificationExecutionId).OnDelete(DeleteBehavior.Restrict);

        // The claimed-set invariant's database backstop, mirroring AttemptInputMessageConfiguration
        // exactly: no attempt may record the same ordered position twice, the same command twice,
        // or the same execution twice — the primary defense is Application-level validation before
        // insert, and these are the database backstops.
        builder.HasIndex(evidence => new { evidence.AttemptId, evidence.Sequence }).IsUnique();
        builder.HasIndex(evidence => new { evidence.AttemptId, evidence.VerificationCommandId }).IsUnique();
        builder.HasIndex(evidence => new { evidence.AttemptId, evidence.VerificationExecutionId }).IsUnique();
    }
}
