using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Queries.GetSealedAgentArtifactWindow;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptArtifactWindow;

/// <summary>
/// One bounded, integrity-verified sealed text window of an Agent attempt selected from the run's
/// history, resolved by the exact <c>(RunId, AttemptId)</c> pair and Agent kind — independent of any
/// collaboration message. An attempt that does not exist in this run or is not an Agent attempt
/// fails with <c>agent_attempts.not_found</c>; every other outcome is an explicit status and never
/// partial content.
/// </summary>
public sealed record GetAgentAttemptArtifactWindowQuery(
    Guid RunId, Guid AttemptId, ArtifactPurpose Purpose, long FromOffset, int MaxBytes)
    : IQuery<Result<GetSealedAgentArtifactWindowQueryResult>>;
