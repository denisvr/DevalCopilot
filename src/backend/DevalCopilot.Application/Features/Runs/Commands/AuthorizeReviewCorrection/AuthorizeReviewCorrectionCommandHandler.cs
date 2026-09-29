using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.AuthorizeReviewCorrection;

public sealed class AuthorizeReviewCorrectionCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    TimeProvider timeProvider,
    IRunEventNotifier? eventNotifier = null)
    : ICommandHandler<AuthorizeReviewCorrectionCommand, Result<AuthorizeReviewCorrectionCommandResult>>
{
    public async Task<Result<AuthorizeReviewCorrectionCommandResult>> HandleAsync(
        AuthorizeReviewCorrectionCommand command, CancellationToken cancellationToken)
    {
        // Guidance is validated by the deterministic Normalize (also called by the validator), before any read or write; null is the
        // bodyless authorization and keeps its default rationale. The rejection never echoes the input.
        var rationale = ReviewCorrectionGuidance.DefaultRationale;
        if (command.Guidance is not null)
        {
            var normalizedGuidance = ReviewCorrectionGuidance.Normalize(command.Guidance);
            if (normalizedGuidance is null)
            {
                return Failure(Error.Failure(
                    "review_correction_authorizations.guidance_invalid",
                    $"The guidance must be non-blank text of at most {ReviewCorrectionGuidance.MaximumLength} characters without control characters or unsafe content."));
            }

            rationale = normalizedGuidance;
        }

        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        if (run is null)
        {
            return Failure(Error.NotFound("runs.not_found", "The requested run was not found."));
        }

        if (run.Lifecycle != RunLifecycle.Running)
        {
            return Failure(Error.Conflict("runs.not_running", "The run is not active."));
        }

        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == run.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Ready)
        {
            return NotCurrent();
        }

        if (!await dbContext.RepositoryMutationLeases.AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return NotCurrent();
        }

        var checkpoint = await dbContext.GitCheckpoints
            .Where(candidate => candidate.WorkspaceId == workspace.Id)
            .OrderByDescending(candidate => candidate.CheckpointNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is null)
        {
            return NotCurrent();
        }

        var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success
            || !string.Equals(evidence.FingerprintSha256, checkpoint.FingerprintSha256, StringComparison.Ordinal))
        {
            return NotCurrent();
        }

        var escalation = await dbContext.ReviewCorrectionEscalations.SingleOrDefaultAsync(
            candidate => candidate.Id == command.EscalationId && candidate.RunId == run.Id, cancellationToken);
        if (escalation is null)
        {
            return Failure(Error.NotFound("review_correction_escalations.not_found", "The requested escalation was not found for this run."));
        }

        var escalationMessage = await dbContext.CollaborationMessages.SingleOrDefaultAsync(
            candidate => candidate.Id == escalation.CollaborationMessageId
                && candidate.RunId == run.Id
                && candidate.Type == CollaborationMessageType.Escalation
                && candidate.Provenance == CollaborationMessageProvenance.HostConstructed,
            cancellationToken);
        if (escalationMessage is null
            || escalationMessage.Actor != ParticipantIdentity.ForOrchestrator()
            || escalationMessage.Recipient != ParticipantIdentity.ForHuman())
        {
            return NotCurrent();
        }

        var snapshot = await ImplementerExecutionReportEligibility.LoadSnapshotAsync(dbContext, run.Id, cancellationToken);
        var currentReview = ReviewCorrectionReviewEligibility.ResolveCurrent(
            snapshot, run.Id, workspace.Id, checkpoint.Id);
        if (currentReview is null
            || currentReview.ReviewAttempt.Id != escalation.ImplementationReviewAttemptId
            || currentReview.ReviewAttempt.AgentCheckpointFingerprintSha256 != checkpoint.FingerprintSha256
            || currentReview.OrderedFindings.Count > ReviewCorrectionOutputSchema.MaximumFindings)
        {
            return NotCurrent();
        }

        IReadOnlyList<Guid> orderedInputIds =
        [
            currentReview.ExecutionReport.Id,
            .. currentReview.OrderedFindings.Select(finding => finding.Id),
        ];
        if (await ReviewCorrectionInputIdentity.HasCompetingSuccessfulCorrectionAsync(
                dbContext, run.Id, Guid.NewGuid(), checkpoint.Id, orderedInputIds, cancellationToken))
        {
            return Failure(Error.Conflict("agent_attempts.already_corrected", "This exact correction input already has a successful correction."));
        }

        if (await dbContext.Attempts.AnyAsync(candidate => candidate.RunId == run.Id && candidate.Status == AttemptStatus.Running, cancellationToken))
        {
            return Failure(Error.Conflict("attempts.run_has_active_attempt", "This run already has an attempt in progress."));
        }

        var existing = await dbContext.ReviewCorrectionAuthorizations
            .Where(candidate => candidate.EscalationId == escalation.Id && candidate.ConsumedByAttemptId == null)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            var existingMismatch = await GuidanceMismatchAsync(run.Id, escalation, existing, rationale, cancellationToken);
            if (existingMismatch is not null)
            {
                return Failure(existingMismatch);
            }

            var existingEventSequence = await FindCollaborationMessageEventSequenceAsync(
                run.Id, existing.HumanInstructionMessageId, cancellationToken);
            if (existingEventSequence is not { } sequence)
            {
                return Failure(Error.Failure(
                    "review_correction_authorizations.event_missing",
                    "The persisted authorization event could not be recovered."));
            }

            if (eventNotifier is not null)
            {
                await eventNotifier.NotifyRunAdvancedAsync(run.Id, sequence, cancellationToken);
            }

            return Result<AuthorizeReviewCorrectionCommandResult>.Success(
                new(existing.EscalationId, existing.HumanInstructionMessageId, existing.Id, "Authorized", sequence));
        }

        var nowUtc = timeProvider.GetUtcNow();
        CollaborationMessage message;
        try
        {
            message = CollaborationMessage.RecordHumanInstruction(
                Guid.NewGuid(), run.Id, escalation.CollaborationMessageId,
                ReviewCorrectionGuidance.BuildStructuredContentJson(rationale), nowUtc);
        }
        catch (ArgumentException)
        {
            return Failure(Error.Failure(
                "review_correction_authorizations.guidance_invalid", "The guidance was not accepted by the instruction policy."));
        }


        var authorization = ReviewCorrectionAuthorization.Create(
            Guid.NewGuid(), run.Id, escalation.Id, message.Id, nowUtc);
        dbContext.CollaborationMessages.Add(message);
        dbContext.ReviewCorrectionAuthorizations.Add(authorization);
        var authorizationEvent = RunEvent.Record(
            Guid.NewGuid(), run.Id, null, RunEventType.CollaborationMessageRecorded, message.Actor,
            JsonSerializer.Serialize(new
            {
                messageId = message.Id,
                type = message.Type.ToString(),
                provenance = message.Provenance.ToString(),
            }), nowUtc);
        dbContext.Events.Add(authorizationEvent);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var concurrent = await dbContext.ReviewCorrectionAuthorizations.AsNoTracking()
                .Where(candidate => candidate.EscalationId == escalation.Id && candidate.ConsumedByAttemptId == null)
                .SingleOrDefaultAsync(cancellationToken);
            if (concurrent is null)
            {
                throw;
            }

            var concurrentMismatch = await GuidanceMismatchAsync(run.Id, escalation, concurrent, rationale, cancellationToken);
            if (concurrentMismatch is not null)
            {
                return Failure(concurrentMismatch);
            }

            var concurrentEventSequence = await FindCollaborationMessageEventSequenceAsync(
                run.Id, concurrent.HumanInstructionMessageId, cancellationToken);
            if (concurrentEventSequence is not { } sequence)
            {
                return Failure(Error.Failure(
                    "review_correction_authorizations.event_missing",
                    "The persisted authorization event could not be recovered."));
            }

            if (eventNotifier is not null)
            {
                await eventNotifier.NotifyRunAdvancedAsync(run.Id, sequence, cancellationToken);
            }

            return Result<AuthorizeReviewCorrectionCommandResult>.Success(
                new(concurrent.EscalationId, concurrent.HumanInstructionMessageId, concurrent.Id, "Authorized", sequence));
        }

        if (eventNotifier is not null)
        {
            await eventNotifier.NotifyRunAdvancedAsync(run.Id, authorizationEvent.Sequence, cancellationToken);
        }

        return Result<AuthorizeReviewCorrectionCommandResult>.Success(
            new(escalation.Id, message.Id, authorization.Id, "Authorized", authorizationEvent.Sequence));
    }

    /// <summary>Validates the existing unconsumed authorization's whole persisted chain (same run and
    /// escalation, expected envelopes, canonical content) and then compares its rationale with the
    /// requested one. Identical intent is idempotent; different guidance is a safe conflict that never
    /// echoes either value; any incoherence fails closed, so a retry never reports success for a
    /// corrupted or foreign authorization.</summary>
    private async Task<Error?> GuidanceMismatchAsync(
        Guid runId,
        ReviewCorrectionEscalation escalation,
        ReviewCorrectionAuthorization authorization,
        string rationale,
        CancellationToken cancellationToken)
    {
        var resolution = await ReviewCorrectionAuthorizationInstruction.ResolveAsync(
            dbContext, runId, escalation, authorization, cancellationToken);
        if (resolution.Error is not null)
        {
            return resolution.Error;
        }

        return string.Equals(resolution.Rationale, rationale, StringComparison.Ordinal)
            ? null
            : Error.Conflict(
                "review_correction_authorizations.guidance_conflict",
                "An authorization with different guidance already exists for this escalation.");
    }

    private async Task<long?> FindCollaborationMessageEventSequenceAsync(
        Guid runId,
        Guid messageId,
        CancellationToken cancellationToken) =>
        await dbContext.Events.AsNoTracking()
            .Where(candidate => candidate.RunId == runId
                && candidate.EventType == RunEventType.CollaborationMessageRecorded
                && candidate.PayloadJson.Contains(messageId.ToString()))
            .Select(candidate => (long?)candidate.Sequence)
            .SingleOrDefaultAsync(cancellationToken);

    private static Result<AuthorizeReviewCorrectionCommandResult> NotCurrent() =>
        Failure(Error.Conflict("review_correction_escalations.not_current", "The escalation no longer belongs to an unresolved review."));

    private static Result<AuthorizeReviewCorrectionCommandResult> Failure(Error error) =>
        Result<AuthorizeReviewCorrectionCommandResult>.Failure(error);
}
