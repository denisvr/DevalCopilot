using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptOutputs;

/// <summary>
/// The references of every collaboration message that records exactly one Agent attempt of a run as its own attempt (ADR-0054), found by
/// the message-level attempt reference alone. It reads a bounded scalar projection of that attempt and of at most 65 of its associated
/// message headers in sequence order; never a summary, content JSON, reply pointer, event, artifact, prompt or provider session, and it
/// infers no expected output from outcome, events or replies. It writes, claims and invokes nothing. An unknown run, a missing or foreign
/// attempt and a non-Agent attempt fail with <c>agent_attempt_outputs.not_found</c>. The whole bounded set is proved before any reference
/// is returned; it has no limit, cursor or omission.
/// </summary>
public sealed record GetAgentAttemptOutputsQuery(Guid RunId, Guid AttemptId) : IQuery<Result<GetAgentAttemptOutputsQueryResult>>;
