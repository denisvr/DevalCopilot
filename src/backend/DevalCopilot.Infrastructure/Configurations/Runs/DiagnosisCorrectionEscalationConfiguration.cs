using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevalCopilot.Infrastructure.Configurations.Runs;

public sealed class DiagnosisCorrectionEscalationConfiguration : IEntityTypeConfiguration<DiagnosisCorrectionEscalation>
{
    public void Configure(EntityTypeBuilder<DiagnosisCorrectionEscalation> builder)
    {
        builder.ToTable("diagnosis_correction_escalations");
        builder.HasKey(escalation => escalation.Id);
        builder.HasIndex(escalation => escalation.VerificationDiagnosisAttemptId).IsUnique();
        builder.HasOne<Run>().WithMany().HasForeignKey(escalation => escalation.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Attempt>().WithMany().HasForeignKey(escalation => escalation.VerificationDiagnosisAttemptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CollaborationMessage>().WithMany().HasForeignKey(escalation => escalation.CollaborationMessageId)
            .HasPrincipalKey(message => message.Id).OnDelete(DeleteBehavior.Restrict);
    }
}
