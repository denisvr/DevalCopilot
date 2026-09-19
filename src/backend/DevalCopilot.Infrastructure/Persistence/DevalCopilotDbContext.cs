using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Infrastructure.Persistence;

public sealed class DevalCopilotDbContext(DbContextOptions<DevalCopilotDbContext> options)
    : DbContext(options), IDevalCopilotDbContext
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<RepositoryBaseline> RepositoryBaselines => Set<RepositoryBaseline>();

    public DbSet<GitWorkspace> GitWorkspaces => Set<GitWorkspace>();

    public DbSet<GitCheckpoint> GitCheckpoints => Set<GitCheckpoint>();

    public DbSet<GitChangedFile> GitChangedFiles => Set<GitChangedFile>();

    public DbSet<VerificationCommand> VerificationCommands => Set<VerificationCommand>();

    public DbSet<VerificationExecution> VerificationExecutions => Set<VerificationExecution>();

    public DbSet<VerificationOutputArtifact> VerificationOutputArtifacts => Set<VerificationOutputArtifact>();

    public DbSet<CheckpointReview> CheckpointReviews => Set<CheckpointReview>();

    public DbSet<CheckpointReviewEvidence> CheckpointReviewEvidence => Set<CheckpointReviewEvidence>();

    public DbSet<RepositoryMutationLease> RepositoryMutationLeases => Set<RepositoryMutationLease>();

    public DbSet<Run> Runs => Set<Run>();

    public DbSet<Attempt> Attempts => Set<Attempt>();

    public DbSet<AttemptInputMessage> AttemptInputMessages => Set<AttemptInputMessage>();

    public DbSet<AttemptVerificationEvidence> AttemptVerificationEvidence => Set<AttemptVerificationEvidence>();

    public DbSet<RunEvent> Events => Set<RunEvent>();

    public DbSet<CollaborationMessage> CollaborationMessages => Set<CollaborationMessage>();

    public DbSet<Artifact> Artifacts => Set<Artifact>();

    public DbSet<HostCapabilitySnapshot> HostCapabilitySnapshots => Set<HostCapabilitySnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DevalCopilotDbContext).Assembly);
    }
}
