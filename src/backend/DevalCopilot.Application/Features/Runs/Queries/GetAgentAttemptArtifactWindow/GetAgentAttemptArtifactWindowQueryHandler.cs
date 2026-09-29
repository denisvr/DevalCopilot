using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetSealedAgentArtifactWindow;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptArtifactWindow;

public sealed class GetAgentAttemptArtifactWindowQueryHandler(IDevalCopilotDbContext dbContext, IArtifactStore artifactStore)
    : IQueryHandler<GetAgentAttemptArtifactWindowQuery, Result<GetSealedAgentArtifactWindowQueryResult>>
{
    public async Task<Result<GetSealedAgentArtifactWindowQueryResult>> HandleAsync(
        GetAgentAttemptArtifactWindowQuery query, CancellationToken cancellationToken)
    {
        if (!SealedAgentArtifactWindowReader.AllowlistedPurposes.Contains(query.Purpose))
        {
            return Result<GetSealedAgentArtifactWindowQueryResult>.Success(
                GetSealedAgentArtifactWindowQueryResult.PurposeNotAllowlisted(query.FromOffset));
        }

        var scalars = await AgentAttemptRead.FindScalarsAsync(dbContext, query.RunId, query.AttemptId, cancellationToken);
        if (scalars is null)
        {
            return Result<GetSealedAgentArtifactWindowQueryResult>.Failure(
                Error.NotFound("agent_attempts.not_found", "The requested Agent attempt was not found."));
        }

        // Null when a persisted enum string is unreadable; treated exactly like an incoherent identity.
        var attempt = await AgentAttemptRead.TryMaterializeAsync(dbContext, scalars.Id, cancellationToken);

        if (attempt is null || !AgentAttemptIdentity.IsCoherent(attempt))
        {
            return Result<GetSealedAgentArtifactWindowQueryResult>.Success(
                GetSealedAgentArtifactWindowQueryResult.AttemptIdentityInvalid(query.FromOffset));
        }

        return Result<GetSealedAgentArtifactWindowQueryResult>.Success(await SealedAgentArtifactWindowReader.ReadAsync(
            dbContext, artifactStore, query.RunId, attempt.Id, query.Purpose, query.FromOffset, query.MaxBytes, cancellationToken));
    }
}
