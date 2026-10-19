using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAdvanceablePlanningSequences;

public sealed class GetAdvanceablePlanningSequencesQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAdvanceablePlanningSequencesQuery, IReadOnlyList<AdvanceablePlanningSequence>>
{
    /// <summary>A hard ceiling on one observation round; the sequences beyond it are observed on the following rounds.</summary>
    public const int MaximumPerRound = 32;

    public async Task<IReadOnlyList<AdvanceablePlanningSequence>> HandleAsync(
        GetAdvanceablePlanningSequencesQuery query, CancellationToken cancellationToken)
    {
        // SQLite cannot order a DateTimeOffset, so the live sequences (a handful) are ordered here, oldest first.
        var live = await dbContext.PlanningSequences.AsNoTracking()
            .Where(candidate => candidate.State == PlanningSequenceState.Armed
                || candidate.State == PlanningSequenceState.PreparingStep
                || candidate.State == PlanningSequenceState.AwaitingStepResult
                || candidate.State == PlanningSequenceState.Stopping)
            .Select(candidate => new { candidate.Id, candidate.RunId, candidate.ProjectId, candidate.State, candidate.ArmedAtUtc })
            .ToListAsync(cancellationToken);
        return live
            .OrderBy(candidate => candidate.ArmedAtUtc)
            .ThenBy(candidate => candidate.Id)
            .Take(MaximumPerRound)
            .Select(candidate => new AdvanceablePlanningSequence(candidate.Id, candidate.RunId, candidate.ProjectId, candidate.State))
            .ToList();
    }
}
