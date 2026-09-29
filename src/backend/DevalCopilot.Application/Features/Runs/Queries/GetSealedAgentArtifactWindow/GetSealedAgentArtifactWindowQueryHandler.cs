using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetSealedAgentArtifactWindow;

public sealed class GetSealedAgentArtifactWindowQueryHandler(IDevalCopilotDbContext dbContext, IArtifactStore artifactStore)
    : IQueryHandler<GetSealedAgentArtifactWindowQuery, Result<GetSealedAgentArtifactWindowQueryResult>>
{

    public async Task<Result<GetSealedAgentArtifactWindowQueryResult>> HandleAsync(
        GetSealedAgentArtifactWindowQuery query, CancellationToken cancellationToken)
    {
        if (!SealedAgentArtifactWindowReader.AllowlistedPurposes.Contains(query.Purpose))
        {
            return Result<GetSealedAgentArtifactWindowQueryResult>.Success(
                GetSealedAgentArtifactWindowQueryResult.PurposeNotAllowlisted(query.FromOffset));
        }

        // Both the message id AND the run id are required together — a message id alone, even a
        // real one, is never trusted to identify the correct run's message. Mirrors
        // GetCollaborationMessageEvidenceQueryHandler's own resolution exactly.
        var message = await dbContext.CollaborationMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == query.MessageId && candidate.RunId == query.RunId, cancellationToken);

        if (message is null)
        {
            return Result<GetSealedAgentArtifactWindowQueryResult>.Failure(
                Error.NotFound("collaboration_messages.not_found", "The requested collaboration message was not found."));
        }

        if (message.Provenance != CollaborationMessageProvenance.ProviderObserved)
        {
            return Result<GetSealedAgentArtifactWindowQueryResult>.Success(
                GetSealedAgentArtifactWindowQueryResult.NoAgentEvidence(query.FromOffset));
        }

        if (message.AttemptId is not { } attemptId)
        {
            return Result<GetSealedAgentArtifactWindowQueryResult>.Success(
                GetSealedAgentArtifactWindowQueryResult.AttemptLinkBroken(query.FromOffset));
        }

        // Resolved via the message's own durable AttemptId foreign key only — never re-derived
        // from role, provider, or attempt number, and never substituted with any other attempt.
        var attempt = await dbContext.Attempts
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == attemptId && candidate.RunId == query.RunId, cancellationToken);

        // Coherence, not mere presence — see GetCollaborationMessageEvidenceQueryHandler's own
        // comment for the full rationale; this repeats the identical check because both
        // operations independently own the same resolution rule for their own request shape.
        if (attempt is null
            || attempt.Kind != AttemptKind.Agent
            || attempt.AgentRole is null
            || attempt.AgentProvider is null
            || attempt.AgentRole != message.ActorAgentRole
            || attempt.AgentProvider != message.ActorAgentProvider)
        {
            return Result<GetSealedAgentArtifactWindowQueryResult>.Success(
                GetSealedAgentArtifactWindowQueryResult.AttemptLinkBroken(query.FromOffset));
        }

        var window = await SealedAgentArtifactWindowReader.ReadAsync(
            dbContext, artifactStore, query.RunId, attempt.Id, query.Purpose, query.FromOffset, query.MaxBytes, cancellationToken);
        return Result<GetSealedAgentArtifactWindowQueryResult>.Success(window);
    }
}
