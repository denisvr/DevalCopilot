using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

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

    DbSet<CheckpointReviewEvidence> CheckpointReviewEvidence { get; }

    DbSet<RepositoryMutationLease> RepositoryMutationLeases { get; }

    DbSet<Run> Runs { get; }

    DbSet<Attempt> Attempts { get; }

    DbSet<AttemptInputMessage> AttemptInputMessages { get; }

    DbSet<AttemptVerificationEvidence> AttemptVerificationEvidence { get; }

    DbSet<RunEvent> Events { get; }

    DbSet<CollaborationMessage> CollaborationMessages { get; }

    DbSet<Artifact> Artifacts { get; }

    DbSet<HostCapabilitySnapshot> HostCapabilitySnapshots { get; }

    DbSet<ReviewCorrectionEscalation> ReviewCorrectionEscalations { get; }

    DbSet<ReviewCorrectionAuthorization> ReviewCorrectionAuthorizations { get; }

    DbSet<DiagnosisCorrectionEscalation> DiagnosisCorrectionEscalations { get; }

    DbSet<PlanningImplementationAuthorization> PlanningImplementationAuthorizations { get; }

    DbSet<LocalCommitOperation> LocalCommitOperations { get; }

    DbSet<LocalCommitAuthorityMember> LocalCommitAuthorityMembers { get; }

    /// <summary>
    /// Change-tracker access for the one claim-time guard that must mark an already-tracked Run's
    /// concurrency-token column as modified (see <c>CurrentClaudeModelPreference</c>).
    /// </summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    /// <summary>
    /// Used only by manual-transaction commands that must read back a database-assigned
    /// value, such as the monotonic event sequence, before returning their result.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens an explicit database transaction. Reserved for a manual-transaction command that must
    /// make a short sequence of its own — after all external I/O has already completed — into one
    /// genuinely atomic database operation: a read (or a conditional guard) whose result feeds a
    /// write that must durably commit together with it, with no other transaction able to commit a
    /// conflicting change to the same row in between. Never used to wrap external I/O. The
    /// underlying connection's lock-wait is bounded (see <c>DevalCopilotDbContext</c>'s own
    /// implementation) so a genuine conflict fails within a bounded time instead of waiting
    /// indefinitely.
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
