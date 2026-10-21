using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAdvanceableLocalExecutionSequences;

public sealed class GetAdvanceableLocalExecutionSequencesQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAdvanceableLocalExecutionSequencesQuery, IReadOnlyList<AdvanceableLocalExecutionSequence>>
{
    /// <summary>A hard ceiling on one observation round; the sequences beyond it are observed on the following rounds.</summary>
    public const int MaximumPerRound = 32;

    public async Task<IReadOnlyList<AdvanceableLocalExecutionSequence>> HandleAsync(
        GetAdvanceableLocalExecutionSequencesQuery query, CancellationToken cancellationToken)
    {
        // SQLite cannot order a DateTimeOffset, so the live sequences (a handful) are ordered here, oldest first.
        var live = await dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.State == LocalExecutionSequenceState.Armed
                || candidate.State == LocalExecutionSequenceState.PreparingStep
                || candidate.State == LocalExecutionSequenceState.AwaitingStepResult
                || candidate.State == LocalExecutionSequenceState.Stopping)
            .Select(candidate => new { candidate.Id, candidate.RunId, candidate.ProjectId, candidate.State, candidate.ArmedAtUtc })
            .ToListAsync(cancellationToken);
        return live
            .OrderBy(candidate => candidate.ArmedAtUtc)
            .ThenBy(candidate => candidate.Id)
            .Take(MaximumPerRound)
            .Select(candidate => new AdvanceableLocalExecutionSequence(candidate.Id, candidate.RunId, candidate.ProjectId, candidate.State))
            .ToList();
    }
}
