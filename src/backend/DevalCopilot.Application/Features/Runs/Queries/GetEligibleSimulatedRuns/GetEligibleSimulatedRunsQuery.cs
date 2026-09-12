using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleSimulatedRuns;

/// <summary>
/// Runs whose intent is recorded but not yet claimed by the hosted supervisor.
/// </summary>
public sealed record GetEligibleSimulatedRunsQuery : IQuery<IReadOnlyList<Guid>>;
