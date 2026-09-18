using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunningAgentAttempts;

/// <summary>
/// Every Agent attempt this instance finds still <c>Running</c> at startup — the same set startup
/// reconciliation is about to mark <c>Interrupted</c>. Read before reconciliation runs, so the
/// caller can recover any sealed-or-partial artifacts for exactly these attempts first.
/// </summary>
public sealed record GetRunningAgentAttemptsQuery : IQuery<IReadOnlyList<RunningAgentAttempt>>;

public sealed record RunningAgentAttempt(Guid RunId, Guid AttemptId);
