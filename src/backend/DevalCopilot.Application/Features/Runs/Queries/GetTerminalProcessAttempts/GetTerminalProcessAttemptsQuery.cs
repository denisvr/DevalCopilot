using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetTerminalProcessAttempts;

/// <summary>
/// Every Process attempt that is not (or no longer) <c>Running</c>. Used only by the narrow
/// startup orphan sweep: a terminal attempt can never legitimately still be capturing output,
/// so a lingering unsealed ".partial" file found for one of these is provably not evidence —
/// nothing durable can ever reference it — and is safe to delete.
/// </summary>
public sealed record GetTerminalProcessAttemptsQuery : IQuery<IReadOnlyList<TerminalProcessAttempt>>;

public sealed record TerminalProcessAttempt(Guid RunId, Guid AttemptId);
