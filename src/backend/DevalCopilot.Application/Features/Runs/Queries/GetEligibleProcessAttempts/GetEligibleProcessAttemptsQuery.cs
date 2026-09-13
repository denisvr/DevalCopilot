using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleProcessAttempts;

/// <summary>
/// Claimed Process attempts not yet executed by this host instance. Startup reconciliation
/// always runs before the supervisor that issues this query, so any Running Process attempt
/// found here was claimed after this instance started, never one orphaned by a prior crash.
/// </summary>
public sealed record GetEligibleProcessAttemptsQuery : IQuery<IReadOnlyList<EligibleProcessAttempt>>;
