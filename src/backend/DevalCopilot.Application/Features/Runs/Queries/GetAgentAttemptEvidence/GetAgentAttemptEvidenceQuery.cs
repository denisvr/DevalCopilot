using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptEvidence;

/// <summary>
/// Bounded evidence metadata for one Agent attempt selected from the run's history, resolved by the
/// exact <c>(RunId, AttemptId)</c> pair and Agent kind alone — independent of any collaboration
/// message. An attempt that does not exist in this run, or is not an Agent attempt, fails with
/// <c>agent_attempts.not_found</c>.
/// </summary>
public sealed record GetAgentAttemptEvidenceQuery(Guid RunId, Guid AttemptId)
    : IQuery<Result<GetAgentAttemptEvidenceQueryResult>>;
