using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;

/// <summary>
/// The joined entities of one run's recorded abandonment read, before the guarded scalar projection of
/// <see cref="RecordedAbandonmentProjection"/> (ADR-0071). It is never materialized: the registered run and project are inner
/// joined and the run's exact stored-key <c>run.abandoned</c> events are a left join, so an event that does not exist is the absence of
/// that row and a damaged one is still a present row. Initializer members, not a constructor, so the query translator can bind each
/// member to its joined entity.
/// </summary>
internal sealed class RecordedAbandonmentJoin
{
    public required Run Run { get; init; }

    public required Project Project { get; init; }

    public required RunEvent AbandonedEvent { get; init; }
}
