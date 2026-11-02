using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// A context whose model is the current one without the ADR-0072 columns, for migration tests that seed or read through the model at
/// a schema older than <c>AddWindowsExecutionProfileEvidence</c>. Those columns are additive and nullable, so ignoring them changes
/// nothing else the older tests prove. The customizer is a replaced service, so this model is cached apart from the real one. The model
/// is deliberately older than the migrations snapshot, so the pending-change guard is ignored; it would otherwise refuse every migration.
/// </summary>
internal static class BeforeExecutionProfileContext
{
    public static DevalCopilotDbContext Create(string connectionString) =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>()
            .UseSqlite(connectionString)
            .ReplaceService<IModelCustomizer, BeforeExecutionProfileModelCustomizer>()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    private sealed class BeforeExecutionProfileModelCustomizer(ModelCustomizerDependencies dependencies)
        : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<Attempt>(attempt =>
            {
                attempt.Ignore(candidate => candidate.AgentInvocationDiagnostic);
                attempt.Ignore(candidate => candidate.AgentInvocationDiagnosticVersion);
                attempt.Ignore(candidate => candidate.AgentStandardOutputCompletion);
                attempt.Ignore(candidate => candidate.AgentStandardErrorCompletion);
                attempt.Ignore(candidate => candidate.AgentStandardInputDelivery);
                attempt.Ignore(candidate => candidate.ProcessStandardOutputCompletion);
                attempt.Ignore(candidate => candidate.ProcessStandardErrorCompletion);
                attempt.Ignore(candidate => candidate.ProcessStandardInputDelivery);
            });
            modelBuilder.Entity<Artifact>().Ignore(artifact => artifact.CaptureCompletion);
            modelBuilder.Entity<VerificationExecution>(execution =>
            {
                execution.Ignore(candidate => candidate.StandardOutputCompletion);
                execution.Ignore(candidate => candidate.StandardErrorCompletion);
                execution.Ignore(candidate => candidate.StandardInputDelivery);
                execution.Ignore(candidate => candidate.EnvironmentProfileVersion);
                execution.Ignore(candidate => candidate.Diagnostic);
            });
            modelBuilder.Entity<VerificationOutputArtifact>().Ignore(artifact => artifact.CaptureCompletion);
        }
    }
}
