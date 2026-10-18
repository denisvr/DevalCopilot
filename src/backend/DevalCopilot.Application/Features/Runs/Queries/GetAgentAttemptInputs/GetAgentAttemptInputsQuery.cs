using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;

/// <summary>
/// The recorded collaboration-input references of exactly one Agent attempt of a run (ADR-0051), independent of any message the attempt
/// produced. It reads bounded scalar projections of that attempt, of at most 65 of its ordered input rows and of at most the 64 messages
/// they reference inside the same run; never a prompt, summary, content JSON, artifact, provider session or any reply, repair or input
/// chain. It writes, claims and invokes nothing. An unknown run, a missing or foreign attempt and a non-Agent attempt fail with
/// <c>agent_attempt_inputs.not_found</c>.
/// </summary>
public sealed record GetAgentAttemptInputsQuery(Guid RunId, Guid AttemptId) : IQuery<Result<GetAgentAttemptInputsQueryResult>>;
