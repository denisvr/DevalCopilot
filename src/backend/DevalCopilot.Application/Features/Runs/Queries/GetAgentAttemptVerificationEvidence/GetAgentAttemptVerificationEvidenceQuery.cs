using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;

/// <summary>
/// The recorded verification membership of exactly one CodeReviewer attempt of a run (ADR-0052), independent of any message it produced:
/// every member's execution metadata and the metadata of its two allowlisted output streams, as read now. It reads bounded scalar
/// projections only, never an artifact path, hash or byte, a membership digest, a current recipe or a later execution, and it writes,
/// claims and invokes nothing. An unknown run and a missing, foreign or non-Agent attempt fail with
/// <c>agent_attempt_verification.not_found</c>.
/// </summary>
public sealed record GetAgentAttemptVerificationEvidenceQuery(Guid RunId, Guid AttemptId)
    : IQuery<Result<GetAgentAttemptVerificationEvidenceQueryResult>>;
