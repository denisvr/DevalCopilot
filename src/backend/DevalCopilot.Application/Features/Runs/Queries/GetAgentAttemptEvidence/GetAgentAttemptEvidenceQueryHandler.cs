using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Application.Features.Runs.Queries.GetSealedAgentArtifactWindow;
using DevalCopilot.Application.Features.Runs.Policies.FormatRepair;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptEvidence;

public sealed class GetAgentAttemptEvidenceQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAgentAttemptEvidenceQuery, Result<GetAgentAttemptEvidenceQueryResult>>
{
    public async Task<Result<GetAgentAttemptEvidenceQueryResult>> HandleAsync(
        GetAgentAttemptEvidenceQuery query, CancellationToken cancellationToken)
    {
        var scalars = await AgentAttemptRead.FindScalarsAsync(dbContext, query.RunId, query.AttemptId, cancellationToken);
        if (scalars is null)
        {
            return Result<GetAgentAttemptEvidenceQueryResult>.Failure(
                Error.NotFound("agent_attempts.not_found", "The requested Agent attempt was not found."));
        }

        // Null when a persisted enum string is unreadable: reported as an unverifiable identity with
        // only the non-enum facts already read, never the exception or the stored string.
        var attempt = await AgentAttemptRead.TryMaterializeAsync(dbContext, scalars.Id, cancellationToken);
        if (attempt is null)
        {
            return Result<GetAgentAttemptEvidenceQueryResult>.Success(new GetAgentAttemptEvidenceQueryResult(
                false, scalars.Id, scalars.AttemptNumber, null, scalars.ClaimedAtUtc, scalars.CompletedAtUtc,
                null, null, null, null, null, null, null, null, []));
        }

        if (!AgentAttemptIdentity.IsCoherent(attempt))
        {
            return Result<GetAgentAttemptEvidenceQueryResult>.Success(new GetAgentAttemptEvidenceQueryResult(
                false, attempt.Id, attempt.AttemptNumber, attempt.Status, attempt.ClaimedAtUtc, attempt.CompletedAtUtc,
                null, null, null, null, null, null, null, null, []));
        }

        // Filtered by AttemptId AND RunId AND the closed purpose allowlist: Artifact.RunId is stored
        // independently of AttemptId, so a cross-run row must never appear here. The unique
        // (AttemptId, Purpose) index bounds this to at most four rows.
        var purposes = SealedAgentArtifactWindowReader.AllowlistedPurposes;
        var rows = await dbContext.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.AttemptId == attempt.Id
                && artifact.RunId == query.RunId
                && purposes.Contains(artifact.Purpose))
            .Select(artifact => new { artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome })
            .ToArrayAsync(cancellationToken);

        var lineage = await AgentRepairLineage.ReadAsync(dbContext, attempt, cancellationToken);

        var artifacts = rows
            .OrderBy(row => row.Purpose)
            .Select(row => new AgentAttemptArtifactMetadata(row.Purpose, row.ByteLength, row.Truncated, row.CaptureOutcome))
            .ToArray();

        return Result<GetAgentAttemptEvidenceQueryResult>.Success(new GetAgentAttemptEvidenceQueryResult(
            true,
            attempt.Id,
            attempt.AttemptNumber,
            attempt.Status,
            attempt.ClaimedAtUtc,
            attempt.CompletedAtUtc,
            attempt.AgentProvider,
            attempt.AgentRole,
            attempt.AgentResponseContract,
            attempt.AgentOutcome,
            attempt.AgentDispatchedAtUtc,
            attempt.AgentTimeout,
            attempt.GetAgentProcessExecutionEvidence(),
            attempt.GetAgentTokenUsageEvidence(),
            artifacts,
            ClaudeMutationTurnLimitFact.ForAttempt(attempt),
            lineage.SourceAttemptId,
            lineage.SourceAttemptNumber));
    }
}
