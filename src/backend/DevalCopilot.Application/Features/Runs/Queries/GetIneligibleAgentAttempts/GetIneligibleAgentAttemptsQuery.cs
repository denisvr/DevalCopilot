using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetIneligibleAgentAttempts;

/// <summary>Identity of one Running, undispatched Agent attempt whose Ready workspace, active
/// mutation lease, or current checkpoint no longer holds — never a prompt, transcript, or
/// credential.</summary>
public sealed record IneligibleAgentAttempt(Guid RunId, Guid AttemptId);

/// <summary>
/// The complement of <c>GetEligibleAgentAttemptsQuery</c>: every Running, undispatched Agent
/// attempt that <em>fails</em> the same workspace/lease/checkpoint gates, so it can be completed
/// with a truthful closed outcome instead of being left to poll forever, never eligible again and
/// never explicitly resolved.
/// </summary>
public sealed record GetIneligibleAgentAttemptsQuery : IQuery<IReadOnlyList<IneligibleAgentAttempt>>;
