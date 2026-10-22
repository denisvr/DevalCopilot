using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAdvanceableLocalExecutionSequences;

public sealed class GetAdvanceableLocalExecutionSequencesQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAdvanceableLocalExecutionSequencesQuery, AdvanceableLocalExecutionSequencesPage>
{
    /// <summary>A hard ceiling on one observation page; the sequences beyond it are observed on the following pages of the same sweep.</summary>
    public const int MaximumPerRound = LocalExecutionSequencePolicy.ObservationPageSize;

    public async Task<AdvanceableLocalExecutionSequencesPage> HandleAsync(
        GetAdvanceableLocalExecutionSequencesQuery query, CancellationToken cancellationToken)
    {
        // The Int64 coordination number is database-orderable (a DateTimeOffset is not under SQLite), so the keyset page is ordered and
        // bounded in the database and never materializes a historical or unbounded set.
        var advancing = dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.State == LocalExecutionSequenceState.Armed
                || candidate.State == LocalExecutionSequenceState.PreparingStep
                || candidate.State == LocalExecutionSequenceState.AwaitingStepResult
                || candidate.State == LocalExecutionSequenceState.Stopping);
        var after = query.AfterCoordinationNumber;
        var highWater = query.HighWaterCoordinationNumber
            ?? await advancing.MaxAsync(candidate => (long?)candidate.CoordinationNumber, cancellationToken)
            ?? after;

        // One more than a page proves whether the finite range has anything left without reading it.
        var page = await advancing
            .Where(candidate => candidate.CoordinationNumber > after && candidate.CoordinationNumber <= highWater)
            .OrderBy(candidate => candidate.CoordinationNumber)
            .Take(MaximumPerRound + 1)
            .Select(candidate => new AdvanceableLocalExecutionSequence(
                candidate.Id, candidate.RunId, candidate.ProjectId, candidate.State, candidate.CoordinationNumber))
            .ToListAsync(cancellationToken);
        var more = page.Count > MaximumPerRound;
        return new AdvanceableLocalExecutionSequencesPage(page.Take(MaximumPerRound).ToList(), highWater, !more);
    }
}
