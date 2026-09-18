using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetTerminalAgentAttempts;

/// <summary>Every Agent attempt already in a terminal state — used at startup to delete any
/// orphaned partial file that no longer has a live attempt to seal it.</summary>
public sealed record GetTerminalAgentAttemptsQuery : IQuery<IReadOnlyList<TerminalAgentAttempt>>;

public sealed record TerminalAgentAttempt(Guid RunId, Guid AttemptId);
