namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// A durable workspace-ownership lock keyed by physical repository identity — not the future
/// Increment 5 executor lease. It carries no expiry, heartbeat, renewal, or automatic sweep in
/// this slice: it stays <see cref="LeaseStatus.Active"/> until an explicit, evidenced
/// <see cref="Release"/> (an in-request compensated failure) or <see cref="Supersede"/> (a
/// reconciliation-detected loss of trust). Both are terminal — a lease row is never reactivated;
/// a new preparation attempt for the same physical repository always creates a new row, so full
/// lease history remains queryable without weakening the active-only exclusivity guarantee
/// (enforced by a database partial unique index on the physical-identity pair, filtered to
/// <see cref="LeaseStatus.Active"/> rows). See ADR-0008.
/// </summary>
public sealed class RepositoryMutationLease
{
    private RepositoryMutationLease()
    {
    }

    public static RepositoryMutationLease Acquire(
        Guid id,
        Guid projectId,
        Guid workspaceId,
        ulong physicalVolumeSerialNumber,
        byte[] physicalFileId,
        DateTimeOffset acquiredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(physicalFileId);

        return new RepositoryMutationLease
        {
            Id = id,
            ProjectId = projectId,
            WorkspaceId = workspaceId,
            PhysicalVolumeSerialNumber = physicalVolumeSerialNumber,
            PhysicalFileId = physicalFileId,
            Status = LeaseStatus.Active,
            AcquiredAtUtc = acquiredAtUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>1:1 with the workspace created atomically alongside this lease — never
    /// re-pointed at a different workspace.</summary>
    public Guid WorkspaceId { get; private set; }

    /// <summary>Denormalized from the project's own resolved identity at acquisition time —
    /// the actual mutual-exclusion key: a database partial unique index on
    /// <c>(PhysicalVolumeSerialNumber, PhysicalFileId)</c> filtered to <c>Status = 'Active'</c>
    /// rows is what enforces "one mutating workspace per physical repository."</summary>
    public ulong PhysicalVolumeSerialNumber { get; private set; }

    public byte[] PhysicalFileId { get; private set; } = [];

    public LeaseStatus Status { get; private set; }

    /// <summary>Display/audit metadata only — never used to determine which lease is
    /// authoritative; that is exactly what <see cref="Status"/> plus the database's partial
    /// unique index already guarantee.</summary>
    public DateTimeOffset AcquiredAtUtc { get; private set; }

    public DateTimeOffset? ReleasedAtUtc { get; private set; }

    public DateTimeOffset? SupersededAtUtc { get; private set; }

    /// <summary>The in-request compensation transition for a proven Git-side-effect or
    /// marker-write failure, or reconciliation's outcome for a workspace that never reached a
    /// trustworthy state. Terminal.</summary>
    public void Release(DateTimeOffset nowUtc)
    {
        if (Status != LeaseStatus.Active)
        {
            throw new InvalidOperationException($"Cannot release a lease that is {Status}.");
        }

        Status = LeaseStatus.Released;
        ReleasedAtUtc = nowUtc;
    }

    /// <summary>Reconciliation's outcome when a previously trustworthy workspace is found
    /// missing or altered. Terminal.</summary>
    public void Supersede(DateTimeOffset nowUtc)
    {
        if (Status != LeaseStatus.Active)
        {
            throw new InvalidOperationException($"Cannot supersede a lease that is {Status}.");
        }

        Status = LeaseStatus.Superseded;
        SupersededAtUtc = nowUtc;
    }
}
