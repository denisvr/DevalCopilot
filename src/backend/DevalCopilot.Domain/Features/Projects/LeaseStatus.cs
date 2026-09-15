namespace DevalCopilot.Domain.Features.Projects;

/// <summary>
/// A <see cref="RepositoryMutationLease"/>'s lifecycle. <see cref="Released"/> and
/// <see cref="Superseded"/> are both terminal: a lease row is never reactivated. There is no
/// <c>Expired</c> value — this slice enforces no time-based expiry, so no state would ever
/// produce it. See ADR-0008.
/// </summary>
public enum LeaseStatus
{
    Active = 0,
    Released = 1,
    Superseded = 2,
}
