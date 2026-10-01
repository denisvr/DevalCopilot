using System.Data.Common;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// A thin decorator over a real <see cref="DevalCopilotDbContext"/> that can be configured to
/// simulate a raw provider failure at exactly one of the two points a claim handler's own
/// transaction boundary depends on and cannot otherwise be made to fail deterministically through
/// the real SQLite provider: acquiring the transaction, or committing it. Every other member —
/// every <c>DbSet&lt;T&gt;</c> and <c>SaveChangesAsync</c> — forwards directly to the real context;
/// this is not an in-memory fake, only a fault-injection seam around those two calls.
/// </summary>
public sealed class FaultInjectingDbContext(DevalCopilotDbContext inner) : IDevalCopilotDbContext
{
    public bool ThrowOnBeginTransaction { get; set; }

    /// <summary>When set, <see cref="BeginTransactionAsync"/> throws <see cref="OperationCanceledException"/>
    /// instead of <see cref="SimulatedDbException"/> — simulating the caller's own cancellation
    /// token firing during acquisition, distinct from a raw provider failure.</summary>
    public bool ThrowCancellationOnBeginTransaction { get; set; }

    public CommitFailureMode CommitFailure { get; set; } = CommitFailureMode.None;

    /// <summary>Runs once, immediately before the real transaction is begun — the
    /// point after every pre-transaction read and external step and before the claim first in-transaction
    /// statement. A change committed from an independent context here is exactly a concurrent commit that landed
    /// at the claim seam, which only in-transaction reads (never a late read before the transaction) can see.</summary>
    public Func<CancellationToken, Task>? BeforeBeginTransaction { get; set; }

    /// <summary>Runs once, immediately before the real <c>SaveChangesAsync</c> (and before any configured
    /// save failure): a change committed from an independent context here is exactly a concurrent commit that
    /// landed between a handler's last read and its single save.</summary>
    public Func<CancellationToken, Task>? BeforeSaveChanges { get; set; }

    public SaveChangesFailureMode SaveChangesFailure { get; set; } = SaveChangesFailureMode.None;

    /// <summary>How the claim's <c>SaveChangesAsync</c> fails: a provider-level update failure that applied
    /// nothing, or the caller's cancellation observed before or after the statements were applied.</summary>
    public enum SaveChangesFailureMode
    {
        None,
        UpdateExceptionBeforeSave,
        CancellationBeforeSave,
        CancellationAfterSave,
    }

    /// <summary>Distinguishes the ways a commit can fail: a genuine failure where nothing actually
    /// persisted, versus the ambiguous-outcome case where the database completed the commit before
    /// the failure became visible to the caller — the exact scenario the handlers' own independent
    /// durability probe after a commit failure exists to resolve. The <c>Cancellation*</c> variants
    /// mirror the same before/after distinction but via <see cref="OperationCanceledException"/>,
    /// simulating the caller's own cancellation token firing during the commit rather than a raw
    /// provider failure. The <c>*RollbackAlsoThrows</c> variants additionally make the handler's own
    /// best-effort rollback of this same transaction throw — proving the handler's ambiguous-outcome
    /// resolution never depends on that rollback having succeeded.</summary>
    public enum CommitFailureMode
    {
        None,
        BeforeCommit,
        AfterCommit,
        CancellationBeforeCommit,
        CancellationAfterCommit,
        BeforeCommitRollbackAlsoThrows,
        AfterCommitRollbackAlsoThrows,
    }

    public DbSet<Project> Projects => inner.Projects;

    public DbSet<RepositoryBaseline> RepositoryBaselines => inner.RepositoryBaselines;

    public DbSet<GitWorkspace> GitWorkspaces => inner.GitWorkspaces;

    public DbSet<GitCheckpoint> GitCheckpoints => inner.GitCheckpoints;

    public DbSet<GitChangedFile> GitChangedFiles => inner.GitChangedFiles;

    public DbSet<VerificationCommand> VerificationCommands => inner.VerificationCommands;

    public DbSet<VerificationExecution> VerificationExecutions => inner.VerificationExecutions;

    public DbSet<VerificationOutputArtifact> VerificationOutputArtifacts => inner.VerificationOutputArtifacts;

    public DbSet<CheckpointReview> CheckpointReviews => inner.CheckpointReviews;

    public DbSet<CheckpointReviewEvidence> CheckpointReviewEvidence => inner.CheckpointReviewEvidence;

    public DbSet<RepositoryMutationLease> RepositoryMutationLeases => inner.RepositoryMutationLeases;

    public DbSet<Run> Runs => inner.Runs;

    public DbSet<Attempt> Attempts => inner.Attempts;

    public DbSet<AttemptInputMessage> AttemptInputMessages => inner.AttemptInputMessages;

    public DbSet<AttemptVerificationEvidence> AttemptVerificationEvidence => inner.AttemptVerificationEvidence;

    public DbSet<RunEvent> Events => inner.Events;

    public DbSet<CollaborationMessage> CollaborationMessages => inner.CollaborationMessages;

    public DbSet<Artifact> Artifacts => inner.Artifacts;

    public DbSet<HostCapabilitySnapshot> HostCapabilitySnapshots => inner.HostCapabilitySnapshots;

    public DbSet<ReviewCorrectionEscalation> ReviewCorrectionEscalations => inner.ReviewCorrectionEscalations;

    public DbSet<ReviewCorrectionAuthorization> ReviewCorrectionAuthorizations => inner.ReviewCorrectionAuthorizations;

    public DbSet<PlanningImplementationAuthorization> PlanningImplementationAuthorizations => inner.PlanningImplementationAuthorizations;

    public Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class => inner.Entry(entity);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (BeforeSaveChanges is { } beforeSave)
        {
            BeforeSaveChanges = null;
            await beforeSave(cancellationToken);
        }

        switch (SaveChangesFailure)
        {
            case SaveChangesFailureMode.UpdateExceptionBeforeSave:
                throw new DbUpdateException("Simulated update failure.", new SimulatedDbException("Simulated update failure."));
            case SaveChangesFailureMode.CancellationBeforeSave:
                throw new OperationCanceledException("Simulated cancellation before saving.");
            case SaveChangesFailureMode.CancellationAfterSave:
                await inner.SaveChangesAsync(cancellationToken);
                throw new OperationCanceledException("Simulated cancellation after saving.");
        }

        return await inner.SaveChangesAsync(cancellationToken);
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (ThrowCancellationOnBeginTransaction)
        {
            throw new OperationCanceledException("Simulated cancellation during transaction acquisition.");
        }

        if (ThrowOnBeginTransaction)
        {
            throw new SimulatedDbException("Simulated transaction acquisition failure.");
        }

        if (BeforeBeginTransaction is { } beforeBegin)
        {
            await beforeBegin(cancellationToken);
        }

        var transaction = await inner.BeginTransactionAsync(cancellationToken);
        return CommitFailure == CommitFailureMode.None ? transaction : new CommitFailingTransaction(transaction, CommitFailure);
    }

    /// <summary>A real <see cref="DbException"/> (the provider-agnostic base type the handlers
    /// themselves catch), not a Sqlite-specific one — this fault-injection seam should never
    /// depend on which provider is really underneath.</summary>
    public sealed class SimulatedDbException(string message) : DbException(message);

    private sealed class CommitFailingTransaction(IDbContextTransaction inner, CommitFailureMode mode) : IDbContextTransaction
    {
        public Guid TransactionId => inner.TransactionId;

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            switch (mode)
            {
                case CommitFailureMode.BeforeCommit:
                case CommitFailureMode.BeforeCommitRollbackAlsoThrows:
                    throw new SimulatedDbException("Simulated commit failure before committing.");
                case CommitFailureMode.CancellationBeforeCommit:
                    throw new OperationCanceledException("Simulated cancellation before committing.");
            }

            await inner.CommitAsync(cancellationToken);

            if (mode == CommitFailureMode.CancellationAfterCommit)
            {
                throw new OperationCanceledException("Simulated cancellation after committing.");
            }

            throw new SimulatedDbException("Simulated commit failure after committing.");
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (mode is CommitFailureMode.BeforeCommitRollbackAlsoThrows or CommitFailureMode.AfterCommitRollbackAlsoThrows)
            {
                // The real underlying transaction is deliberately left for Dispose(Async) to
                // resolve (an uncommitted ADO.NET transaction is rolled back on disposal even
                // without an explicit Rollback call) — this simulates only the handler's own
                // best-effort RollbackAsync call itself failing, not a real inability to release
                // the connection's lock.
                throw new SimulatedDbException("Simulated rollback failure.");
            }

            return inner.RollbackAsync(cancellationToken);
        }

        public void Commit() => throw new SimulatedDbException("Simulated commit failure.");

        public void Rollback() => inner.Rollback();

        public void Dispose() => inner.Dispose();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
