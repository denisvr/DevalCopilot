using System.Text;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;

/// <summary>Claims a correction only from a completed, applicable changes-requested review. The
/// exact ordered input messages are persisted before any external invocation can occur.</summary>
public sealed class CreateReviewCorrectionAttemptCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    IArtifactStore artifactStore,
    TimeProvider timeProvider)
    : ICommandHandler<CreateReviewCorrectionAttemptCommand, Result<CreateReviewCorrectionAttemptCommandResult>>
{
    private const int MaxContextManifestBytes = 32 * 1024;
    private static readonly TimeSpan InvocationTimeout = TimeSpan.FromMinutes(20);
    private const int MaxBytesPerStream = 256 * 1024;
    private const int MaxTotalCapturedBytes = 512 * 1024;

    public async Task<Result<CreateReviewCorrectionAttemptCommandResult>> HandleAsync(
        CreateReviewCorrectionAttemptCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Failure(Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Failure(Error.Conflict("runs.not_running", "The run is not active."));
        }

        if (await dbContext.Attempts.AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken))
        {
            return Failure(Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
        }

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return Failure(Error.Conflict("agent_attempts.workspace_not_ready", "A ready isolated workspace is required."));
        }

        if (!await dbContext.RepositoryMutationLeases.AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return Failure(Error.Conflict("agent_attempts.lease_not_active", "An active workspace lease is required."));
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return Failure(Error.Conflict("agent_attempts.checkpoint_missing", "A current Git checkpoint is required."));
        }

        var claudeSnapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.ClaudeCli, cancellationToken);
        if (claudeSnapshot is null
            || claudeSnapshot.ReasonCode != CapabilityProbeReason.None
            || string.IsNullOrWhiteSpace(claudeSnapshot.ResolvedExecutablePath))
        {
            return Failure(Error.Conflict("agent_attempts.provider_not_observed", "The Claude runtime is not currently observed as available."));
        }

        var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success
            || !string.Equals(evidence.FingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(Error.Conflict("agent_attempts.checkpoint_not_current", "The selected source checkpoint is no longer current."));
        }

        var review = await dbContext.Attempts.SingleOrDefaultAsync(
            candidate => candidate.Id == command.ImplementationReviewAttemptId && candidate.RunId == run.Id,
            cancellationToken);
        if (review is null
            || review.Kind != AttemptKind.Agent
            || review.AgentRole != AgentRole.CodeReviewer
            || review.AgentResponseContract != AgentResponseContract.ImplementationReview
            || review.Status != AttemptStatus.Completed
            || review.AgentOutcome != AgentOutcome.ReviewChangesRequested
            || review.AgentGitWorkspaceId != workspace.Id
            || review.AgentGitCheckpointId != checkpoint.Id
            || !string.Equals(review.AgentCheckpointFingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return Failure(Error.Conflict("agent_attempts.review_not_applicable", "The selected review is not an applicable changes-requested review."));
        }

        var orderedReviewInputs = await ReviewCorrectionInputIdentity.GetOrderedInputMessageIdsAsync(
            dbContext, review.Id, cancellationToken);
        if (orderedReviewInputs.Count != 1)
        {
            return Failure(Error.Conflict("agent_attempts.review_input_invalid", "The selected review does not have one exact implementation input."));
        }

        var executionReport = await dbContext.CollaborationMessages.SingleOrDefaultAsync(
            message => message.Id == orderedReviewInputs[0] && message.RunId == run.Id,
            cancellationToken);
        if (executionReport is null || executionReport.Type != CollaborationMessageType.ExecutionReport)
        {
            return Failure(Error.Conflict("agent_attempts.execution_report_invalid", "The reviewed execution report is not valid for correction."));
        }

        var implementationAttempt = await AgentAuthoredMessageEligibility.ResolveOwningAttemptAsync(
            dbContext, executionReport, run.Id, AgentRole.Implementer, cancellationToken);
        if (implementationAttempt is null
            || implementationAttempt.AgentResponseContract != AgentResponseContract.ImplementationReport
            || implementationAttempt.Status != AttemptStatus.Completed
            || implementationAttempt.AgentOutcome != AgentOutcome.Implemented
            || implementationAttempt.AgentGitWorkspaceId != workspace.Id
            || implementationAttempt.AgentResultGitCheckpointId != checkpoint.Id)
        {
            return Failure(Error.Conflict("agent_attempts.implementation_not_applicable", "The reviewed implementation is not valid for correction."));
        }

        var findings = await dbContext.CollaborationMessages
            .Where(message =>
                message.AttemptId == review.Id
                && message.RunId == run.Id
                && message.Type == CollaborationMessageType.ReviewFinding
                && message.InReplyToMessageId == executionReport.Id
                && message.Provenance == CollaborationMessageProvenance.ProviderObserved)
            .OrderBy(message => message.Sequence)
            .ToListAsync(cancellationToken);
        var reviewActor = review.AgentProvider is { } reviewProvider
            ? ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, reviewProvider)
            : null;
        if (findings.Count is < 1 or > ReviewCorrectionOutputSchema.MaximumFindings
            || reviewActor is null
            || findings.Any(finding => finding.Actor != reviewActor))
        {
            return Failure(Error.Conflict("agent_attempts.review_findings_invalid", "The review has no valid bounded finding set."));
        }

        var attemptId = Guid.NewGuid();
        if (await ReviewCorrectionInputIdentity.HasCompetingSuccessfulCorrectionAsync(
                dbContext,
                run.Id,
                attemptId,
                checkpoint.Id,
                [executionReport.Id, .. findings.Select(finding => finding.Id)],
                cancellationToken))
        {
            return Failure(Error.Conflict("agent_attempts.already_corrected", "This exact correction input already has a successful correction."));
        }

        var manifestJson = ReviewCorrectionContextManifestBuilder.Build(
            run.ProjectId,
            run.Id,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            run.Objective,
            executionReport.Id,
            executionReport.Summary,
            executionReport.StructuredContentJson,
            findings.Select(finding => new ReviewCorrectionContextManifestBuilder.Finding(
                finding.Id, finding.Summary, finding.StructuredContentJson)).ToArray(),
            evidence.ChangedPaths,
            evidence.CompleteDiff);
        if (Encoding.UTF8.GetByteCount(manifestJson) > MaxContextManifestBytes)
        {
            return Failure(Error.Failure("agent_attempts.context_manifest_too_large", "The context manifest exceeds its bound."));
        }

        var manifestArtifactId = Guid.NewGuid();
        var manifestPartialPath = artifactStore.GetPartialPath(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPartialPath)!);
        await File.WriteAllTextAsync(manifestPartialPath, manifestJson, cancellationToken);
        var sealedManifest = await artifactStore.SealAsync(run.Id, attemptId, ArtifactPurpose.AgentContextManifest, cancellationToken);
        if (sealedManifest is null)
        {
            return Failure(Error.Failure("agent_attempts.context_manifest_seal_failed", "The context manifest could not be sealed."));
        }

        var attemptNumber = await dbContext.Attempts.Where(candidate => candidate.RunId == run.Id).CountAsync(cancellationToken) + 1;
        var nowUtc = timeProvider.GetUtcNow();
        var attempt = Attempt.ClaimAgentReviewCorrection(
            attemptId,
            run.Id,
            attemptNumber,
            workspace.Id,
            checkpoint.Id,
            checkpoint.FingerprintSha256,
            manifestArtifactId,
            InvocationTimeout,
            MaxBytesPerStream,
            MaxTotalCapturedBytes,
            nowUtc);
        dbContext.Attempts.Add(attempt);

        var inputRows = new List<AttemptInputMessage>(findings.Count + 1)
        {
            AttemptInputMessage.Record(Guid.NewGuid(), attemptId, executionReport.Id, 0),
        };
        inputRows.AddRange(findings.Select((finding, index) => AttemptInputMessage.Record(
            Guid.NewGuid(), attemptId, finding.Id, index + 1)));
        dbContext.AttemptInputMessages.AddRange(inputRows);

        var manifestArtifact = Artifact.Record(
            manifestArtifactId,
            run.Id,
            attemptId,
            ArtifactPurpose.AgentContextManifest,
            "application/json",
            sealedManifest.RelativeStoragePath,
            sealedManifest.ContentHash,
            sealedManifest.ByteLength,
            false,
            ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.HostConstructedContent,
            ArtifactRetentionPolicy.RetainUntilRunDeleted,
            nowUtc);
        dbContext.Artifacts.Add(manifestArtifact);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var thisAttemptPersisted = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.Id == attemptId, cancellationToken);
            if (thisAttemptPersisted)
            {
                return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
                    new CreateReviewCorrectionAttemptCommandResult(attemptId, attemptNumber));
            }

            artifactStore.DeleteOrphanedSealedFile(run.Id, attemptId, ArtifactPurpose.AgentContextManifest);
            dbContext.Attempts.Remove(attempt);
            foreach (var inputMessage in inputRows)
            {
                dbContext.AttemptInputMessages.Remove(inputMessage);
            }

            dbContext.Artifacts.Remove(manifestArtifact);

            var competingRunningAttemptExists = await dbContext.Attempts
                .AsNoTracking()
                .AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken);
            if (competingRunningAttemptExists)
            {
                return Failure(Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
            }

            return Failure(Error.Failure("attempts.persistence_failed", "The correction attempt could not be durably recorded."));
        }

        return Result<CreateReviewCorrectionAttemptCommandResult>.Success(
            new CreateReviewCorrectionAttemptCommandResult(attempt.Id, attempt.AttemptNumber));
    }

    private static Result<CreateReviewCorrectionAttemptCommandResult> Failure(Error error) =>
        Result<CreateReviewCorrectionAttemptCommandResult>.Failure(error);
}
