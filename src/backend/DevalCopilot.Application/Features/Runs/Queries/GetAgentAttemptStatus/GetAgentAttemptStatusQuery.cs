using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;

/// <summary>Bounded status and artifact metadata for the most recent Codex planning attempt on a
/// run — never a path, prompt, transcript, or credential. An unknown run fails with
/// <c>runs.not_found</c>; an existing run with no such attempt yet succeeds with an explicit
/// <c>HasAttempt: false</c> result — never an ambiguous null body.</summary>
public sealed record GetAgentAttemptStatusQuery(Guid RunId) : IQuery<Result<AgentAttemptStatusQueryResult>>;
