using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptStatus;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationMessageEvidence;

public sealed class GetCollaborationMessageEvidenceQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetCollaborationMessageEvidenceQuery, Result<CollaborationMessageEvidenceQueryResult>>
{
    /// <summary>A small, bounded cap on how many artifact metadata rows one evidence response ever
    /// returns — never a silent truncation; see <see cref="CollaborationMessageEvidenceQueryResult.ArtifactsOmitted"/>.
    /// A single attempt can never durably hold more than <c>ArtifactPurpose</c>'s own member count
    /// (the unique (AttemptId, Purpose) index enforces at most one artifact per purpose), so this
    /// cap is a defensive bound against a future purpose being added, not a limit this schema can
    /// exceed today.</summary>
    private const int MaxArtifacts = 5;

    public async Task<Result<CollaborationMessageEvidenceQueryResult>> HandleAsync(
        GetCollaborationMessageEvidenceQuery query, CancellationToken cancellationToken)
    {
        // Both the message id AND the run id are required together — a message id alone, even a
        // real one, is never trusted to identify the correct run's message.
        var message = await dbContext.CollaborationMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == query.MessageId && candidate.RunId == query.RunId, cancellationToken);

        if (message is null)
        {
            return Result<CollaborationMessageEvidenceQueryResult>.Failure(
                Error.NotFound("collaboration_messages.not_found", "The requested collaboration message was not found."));
        }

        if (message.Provenance != CollaborationMessageProvenance.ProviderObserved)
        {
            // A legitimate Human/Orchestrator/Simulated message — never an error, and never
            // reported the same way as a broken link. A Simulated message may still carry its own
            // non-null AttemptId (linking to a Simulated, not Agent, attempt); Provenance, not
            // AttemptId nullability, is the only trustworthy discriminator here.
            return Result<CollaborationMessageEvidenceQueryResult>.Success(CollaborationMessageEvidenceQueryResult.NoAgentEvidence);
        }

        if (message.AttemptId is not { } attemptId)
        {
            // ProviderObserved should always carry a real AttemptId per
            // CollaborationMessage.RecordAgent's own construction invariant; if it is ever
            // observed missing, fail closed rather than reporting a misleadingly benign
            // "no evidence" state for what should have been provider-observed evidence.
            return Result<CollaborationMessageEvidenceQueryResult>.Success(CollaborationMessageEvidenceQueryResult.AttemptLinkBroken);
        }

        // Resolved via the message's own durable AttemptId foreign key only — never re-derived
        // from role, provider, or attempt number, and never substituted with any other attempt.
        var attempt = await dbContext.Attempts
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == attemptId && candidate.RunId == query.RunId, cancellationToken);

        // Coherence, not mere presence: the resolved row must actually be the Agent attempt this
        // ProviderObserved message's own actor claims. A missing row, a non-Agent attempt (e.g. a
        // corrupted link pointing at a Simulated/Process row), or a role/provider mismatch are all
        // equally untrustworthy — every one of them fails closed to the same distinct status rather
        // than substituting the wrong attempt's evidence.
        if (attempt is null
            || attempt.Kind != AttemptKind.Agent
            || attempt.AgentRole != message.ActorAgentRole
            || attempt.AgentProvider != message.ActorAgentProvider)
        {
            return Result<CollaborationMessageEvidenceQueryResult>.Success(CollaborationMessageEvidenceQueryResult.AttemptLinkBroken);
        }

        // SQLite cannot translate an ORDER BY over a DateTimeOffset column, and a single attempt
        // can never durably hold more than a handful of artifact rows (see MaxArtifacts' own
        // doc comment), so the bounded ordering-then-capping happens client-side rather than in
        // SQL.
        // Filtered by both AttemptId AND RunId — Artifact.RunId is persisted independently of its
        // AttemptId and the database enforces no composite run/attempt ownership constraint
        // between them, so an inconsistent cross-run row (a real persistence bug, or a future
        // migration mistake) must never leak into this run's evidence or inflate its counts.
        var allArtifacts = await dbContext.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.AttemptId == attempt.Id && artifact.RunId == query.RunId)
            .Select(artifact => new { artifact.CreatedAtUtc, artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome })
            .ToArrayAsync(cancellationToken);

        var totalArtifactCount = allArtifacts.Length;
        var artifacts = allArtifacts
            .OrderBy(artifact => artifact.CreatedAtUtc)
            .Take(MaxArtifacts)
            .Select(artifact => new AgentAttemptArtifactMetadata(artifact.Purpose, artifact.ByteLength, artifact.Truncated, artifact.CaptureOutcome))
            .ToArray();

        // The result checkpoint's own fingerprint, resolved only when the checkpoint row exists AND
        // belongs to this attempt's own workspace — an incoherent or missing reference fails safely
        // by leaving the fingerprint null rather than ever substituting a fingerprint that does not
        // truthfully belong to this attempt's own result checkpoint.
        string? resultCheckpointFingerprintSha256 = null;
        if (attempt.AgentResultGitCheckpointId is { } resultGitCheckpointId)
        {
            var resultCheckpoint = await dbContext.GitCheckpoints
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == resultGitCheckpointId, cancellationToken);
            if (resultCheckpoint is not null && resultCheckpoint.WorkspaceId == attempt.AgentGitWorkspaceId)
            {
                resultCheckpointFingerprintSha256 = resultCheckpoint.FingerprintSha256;
            }
        }

        return Result<CollaborationMessageEvidenceQueryResult>.Success(new CollaborationMessageEvidenceQueryResult(
            CollaborationMessageEvidenceStatus.HasEvidence,
            attempt.Id,
            attempt.AttemptNumber,
            attempt.Kind,
            attempt.Status,
            attempt.ClaimedAtUtc,
            attempt.CompletedAtUtc,
            attempt.AgentProvider,
            attempt.AgentRole,
            attempt.AgentResponseContract,
            attempt.AgentOutcome,
            attempt.AgentDispatchedAtUtc,
            attempt.AgentGitCheckpointId,
            attempt.AgentCheckpointFingerprintSha256,
            attempt.AgentResultGitCheckpointId,
            resultCheckpointFingerprintSha256,
            attempt.AgentTimeout,
            attempt.GetAgentProcessExecutionEvidence(),
            attempt.GetAgentTokenUsageEvidence(),
            artifacts,
            totalArtifactCount > MaxArtifacts,
            totalArtifactCount));
    }
}
