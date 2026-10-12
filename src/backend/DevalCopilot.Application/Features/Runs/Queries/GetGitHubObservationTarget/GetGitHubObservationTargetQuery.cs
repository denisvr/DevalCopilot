using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubObservationTarget;

/// <summary>Read-only state of a run's explicitly selected GitHub observation scope (ADR-0040). It reads persisted rows only through
/// untracked projections: no Git, filesystem, process, provider or network, and it writes, locks and repairs nothing.</summary>
public sealed record GetGitHubObservationTargetQuery(Guid RunId) : IQuery<Result<GetGitHubObservationTargetQueryResult>>;
