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
    /// <summary>The closed set of Agent-attempt artifact purposes this operation ever serves —
    /// never the two Process-attempt purposes, which belong to a different attempt kind and a
    /// different existing endpoint (<c>GetProcessAttemptOutput</c>). Defense in depth: the API
    /// boundary already restricts its route to these same four values.</summary>
    private static readonly IReadOnlySet<ArtifactPurpose> AllowlistedPurposes = new HashSet<ArtifactPurpose>
    {
        ArtifactPurpose.AgentContextManifest,
        ArtifactPurpose.AgentStandardOutput,
        ArtifactPurpose.AgentStandardError,
        ArtifactPurpose.AgentFinalResponse,
    };

    public async Task<Result<GetSealedAgentArtifactWindowQueryResult>> HandleAsync(
        GetSealedAgentArtifactWindowQuery query, CancellationToken cancellationToken)
    {
        if (!AllowlistedPurposes.Contains(query.Purpose))
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

        // Filtered by AttemptId, RunId, AND Purpose — Artifact.RunId is persisted independently of
        // its AttemptId and the database enforces no composite run/attempt ownership constraint
        // between them, so an inconsistent cross-run row must never leak into this window.
        var artifact = await dbContext.Artifacts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.AttemptId == attempt.Id
                    && candidate.RunId == query.RunId
                    && candidate.Purpose == query.Purpose,
                cancellationToken);

        if (artifact is null)
        {
            return Result<GetSealedAgentArtifactWindowQueryResult>.Success(
                GetSealedAgentArtifactWindowQueryResult.ArtifactNotFound(query.FromOffset));
        }

        var sealedRead = await artifactStore.VerifyAndReadSealedAsync(
            artifact.RelativeStoragePath, artifact.ByteLength, artifact.ContentHash, query.FromOffset, query.MaxBytes, cancellationToken);

        var status = sealedRead.Status switch
        {
            SealedReadStatus.Ok => SealedAgentArtifactWindowStatus.Ok,
            SealedReadStatus.Missing => SealedAgentArtifactWindowStatus.Missing,
            SealedReadStatus.IntegrityMismatch => SealedAgentArtifactWindowStatus.IntegrityMismatch,
            _ => throw new ArgumentOutOfRangeException(),
        };

        // Truncated is preserved as-is (including a genuinely unknown null for an artifact
        // recovered from a host interruption) only for a verified Ok read; every other status
        // never carries content, so it never carries a truncation fact about content it isn't
        // returning.
        return Result<GetSealedAgentArtifactWindowQueryResult>.Success(new GetSealedAgentArtifactWindowQueryResult(
            status,
            sealedRead.Text,
            sealedRead.NextOffset,
            sealedRead.TotalLengthSoFar,
            status == SealedAgentArtifactWindowStatus.Ok ? artifact.Truncated : null));
    }
}
