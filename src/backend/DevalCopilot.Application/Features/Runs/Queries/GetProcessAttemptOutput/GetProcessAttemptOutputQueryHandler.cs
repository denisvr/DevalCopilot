using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetProcessAttemptOutput;

public sealed class GetProcessAttemptOutputQueryHandler(IDevalCopilotDbContext dbContext, IArtifactStore artifactStore)
    : IQueryHandler<GetProcessAttemptOutputQuery, GetProcessAttemptOutputQueryResult>
{
    public async Task<GetProcessAttemptOutputQueryResult> HandleAsync(
        GetProcessAttemptOutputQuery query, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Attempts
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.AttemptId && candidate.RunId == query.RunId, cancellationToken);

        if (attempt is null)
        {
            return new GetProcessAttemptOutputQueryResult(
                ProcessAttemptOutputStatus.AttemptNotFound, string.Empty, query.FromOffset, 0, IsFinal: true, Truncated: false);
        }

        var artifact = await dbContext.Artifacts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.AttemptId == query.AttemptId && candidate.Purpose == query.Purpose, cancellationToken);

        if (artifact is not null)
        {
            var sealedRead = await artifactStore.VerifyAndReadSealedAsync(
                artifact.RelativeStoragePath, artifact.ByteLength, artifact.ContentHash, query.FromOffset, query.MaxBytes, cancellationToken);

            var status = sealedRead.Status switch
            {
                SealedReadStatus.Ok => ProcessAttemptOutputStatus.Ok,
                SealedReadStatus.Missing => ProcessAttemptOutputStatus.NoOutputAvailable,
                SealedReadStatus.IntegrityMismatch => ProcessAttemptOutputStatus.IntegrityMismatch,
                _ => throw new ArgumentOutOfRangeException(),
            };

            // Preserved as-is, including a genuinely unknown (null) truncation for an artifact
            // recovered from a host interruption — never coalesced to a false claim of "known
            // not truncated".
            return new GetProcessAttemptOutputQueryResult(
                status, sealedRead.Text, sealedRead.NextOffset, sealedRead.TotalLengthSoFar, IsFinal: true, artifact.Truncated);
        }

        if (attempt.Status == AttemptStatus.Running)
        {
            var partialRead = await artifactStore.ReadPartialAsync(
                query.RunId, query.AttemptId, query.Purpose, query.FromOffset, query.MaxBytes, cancellationToken);

            return new GetProcessAttemptOutputQueryResult(
                ProcessAttemptOutputStatus.Ok, partialRead.Text, partialRead.NextOffset, partialRead.TotalLengthSoFar,
                IsFinal: false, Truncated: false);
        }

        // Terminal, but no artifact was ever recorded for this stream (not a Process attempt,
        // or its seal failed) — truthfully nothing to show, not an error.
        return new GetProcessAttemptOutputQueryResult(
            ProcessAttemptOutputStatus.NoOutputAvailable, string.Empty, query.FromOffset, 0, IsFinal: true, Truncated: false);
    }
}
