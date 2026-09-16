using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Data;

public interface IDevalCopilotDbContext
{
    DbSet<Project> Projects { get; }

    DbSet<RepositoryBaseline> RepositoryBaselines { get; }

    DbSet<GitWorkspace> GitWorkspaces { get; }

    DbSet<GitCheckpoint> GitCheckpoints { get; }

    DbSet<GitChangedFile> GitChangedFiles { get; }

    DbSet<VerificationCommand> VerificationCommands { get; }

    DbSet<VerificationExecution> VerificationExecutions { get; }

    DbSet<VerificationOutputArtifact> VerificationOutputArtifacts { get; }

    DbSet<CheckpointReview> CheckpointReviews { get; }

    DbSet<RepositoryMutationLease> RepositoryMutationLeases { get; }

    DbSet<Run> Runs { get; }

    DbSet<Attempt> Attempts { get; }

    DbSet<RunEvent> Events { get; }

    DbSet<Artifact> Artifacts { get; }

    DbSet<HostCapabilitySnapshot> HostCapabilitySnapshots { get; }

    /// <summary>
    /// Used only by manual-transaction commands that must read back a database-assigned
    /// value, such as the monotonic event sequence, before returning their result.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
