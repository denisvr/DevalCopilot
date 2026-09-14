using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunningProcessAttempts;

/// <summary>
/// Every Process attempt this instance finds still <c>Running</c> at startup — the same set
/// startup reconciliation is about to mark <c>Interrupted</c>. Read before reconciliation runs,
/// so the caller can recover any sealed-or-partial output for exactly these attempts first.
/// </summary>
public sealed record GetRunningProcessAttemptsQuery : IQuery<IReadOnlyList<RunningProcessAttempt>>;

public sealed record RunningProcessAttempt(Guid RunId, Guid AttemptId);
