using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies;

/// <summary>
/// Claim-time snapshot and guard for the Run's own current Claude agentic-turn-limit request, shared by
/// the initial implementation and review correction claim handlers so they read and guard it
/// identically. Each handler loads its tracked <c>Run</c> at the start of the request, long before its
/// external Git capture and manifest sealing, so reading that instance late would return whatever was
/// current at the early load. <see cref="ReadAndGuardAsync"/> instead reads the stored text afresh,
/// untracked, as late as possible (after all external work, immediately before the Attempt is
/// constructed), then marks the tracked Run's property modified with that fresh value as its original
/// value. The stored text is an EF concurrency token, so the claim's single
/// <c>SaveChangesAsync</c> emits an <c>UPDATE runs ... WHERE Id, Lifecycle, ..., RequestedClaudeMaxTurns</c>
/// requiring the exact value the Attempt snapshotted; a request change committed after the read matches
/// zero rows and the whole batch (Attempt, inputs, artifact, any correction authorization consumption)
/// rolls back. No external I/O happens between the read and the save.
///
/// <para>
/// The column is read as its exact stored text and parsed strictly (<see cref="ClaudeMutationTurnLimit.Read"/>), so
/// a fractional, overflowing, or non-numeric stored value is refused rather than truncated, clamped, read as
/// zero or null, or allowed to overflow during materialization.
/// </para>
/// </summary>
public static class CurrentClaudeMutationTurnLimit
{
    public static async Task<Result<int?>> ReadAndGuardAsync(
        IDevalCopilotDbContext dbContext, Run run, CancellationToken cancellationToken)
    {
        var stored = await dbContext.Runs
            .AsNoTracking()
            .Where(candidate => candidate.Id == run.Id)
            .Select(candidate => EF.Property<string?>(candidate, Run.RequestedClaudeMaxTurnsStorageProperty))
            .SingleAsync(cancellationToken);

        var reading = ClaudeMutationTurnLimit.Read(stored);
        if (reading.IsMalformed)
        {
            return Result<int?>.Failure(ClaudeMutationTurnLimitErrors.PersistedLimitInvalid());
        }

        var property = dbContext.Entry(run).Property<string?>(Run.RequestedClaudeMaxTurnsStorageProperty);
        property.OriginalValue = stored;
        property.CurrentValue = stored;
        property.IsModified = true;

        return Result<int?>.Success(reading.Value);
    }
}
