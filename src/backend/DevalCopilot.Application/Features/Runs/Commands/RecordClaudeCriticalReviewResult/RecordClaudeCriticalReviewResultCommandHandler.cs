using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;

public sealed class RecordClaudeCriticalReviewResultCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordClaudeCriticalReviewResultCommand, Result<RecordClaudeCriticalReviewResultCommandResult>>
{
    private static readonly IReadOnlySet<ArtifactPurpose> SupportedResultArtifactPurposes = new HashSet<ArtifactPurpose>
    {
        ArtifactPurpose.AgentStandardOutput,
        ArtifactPurpose.AgentStandardError,
        ArtifactPurpose.AgentFinalResponse,
    };

    /// <summary>
    /// The explicit, closed policy for what a Claude critical-review attempt may report directly
    /// through this command. <see cref="AgentOutcome.SourceChanged"/> and
    /// <see cref="AgentOutcome.WorkspaceNoLongerEligible"/> are derived exclusively by their own
    /// dedicated pre-dispatch commands; <see cref="AgentOutcome.InputAlreadyReviewed"/> is derived
    /// exclusively by <c>RecordClaudeCriticalReviewInputAlreadyReviewedCommand</c>, which
    /// independently re-verifies the competing review before ever recording it; and
    /// <see cref="AgentOutcome.Proposed"/> belongs to the Codex planning contract, never this one.
    /// Checking membership in this set — rather than singling out one excluded value at a time —
    /// is what makes the rejection exhaustive: every outcome this handler does not explicitly
    /// allow is rejected the same safe way, with zero mutation, before
    /// <see cref="Attempt.CompleteAgent"/> is ever called. That call remains an independent
    /// Domain-level backstop, never the only line of defense.
    /// </summary>
    private static readonly IReadOnlySet<AgentOutcome> CallerSelectableOutcomes = new HashSet<AgentOutcome>
    {
        AgentOutcome.Accepted,
        AgentOutcome.Challenged,
        AgentOutcome.InvalidStructuredOutput,
        AgentOutcome.ProviderInvocationFailed,
        AgentOutcome.CheckpointEvidenceUnavailable,
    };

    /// <summary>Matches both the EF column bound and the adapter's own scan bound — never
    /// silently truncated or left to fail unpredictably at save time.</summary>
    private const int MaxProviderSessionIdLength = 256;

    public async Task<Result<RecordClaudeCriticalReviewResultCommandResult>> HandleAsync(
        RecordClaudeCriticalReviewResultCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (run is null || attempt is null || attempt.RunId != run.Id)
        {
            return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run or attempt was not found."));
        }

        if (attempt.Kind != AttemptKind.Agent || attempt.AgentResponseContract != AgentResponseContract.CriticalReview)
        {
            return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(
                Error.Conflict("attempts.not_critical_review", "The attempt is not a Claude critical-review attempt."));
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot record a result."));
        }

        // Everything from here through the artifact-shape check is pure validation: no mutation
        // happens until every one of these has passed, so any rejection leaves the attempt, its
        // artifacts, and the ledger completely untouched — never a partial mutation followed by a
        // thrown exception.
        if (!attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(
                Error.Conflict(
                    "agent_attempts.not_dispatched",
                    "A provider result cannot be recorded for an attempt that was never dispatched."));
        }

        if (!Enum.IsDefined(command.Outcome))
        {
            return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(
                Error.Failure("agent_attempts.invalid_outcome", "The reported outcome is not a defined agent outcome."));
        }

        if (!CallerSelectableOutcomes.Contains(command.Outcome))
        {
            return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.outcome_not_caller_selectable",
                    $"{command.Outcome} belongs to a dedicated host-derived recording path and cannot be reported directly here."));
        }

        if (command.Outcome is AgentOutcome.Accepted or AgentOutcome.Challenged)
        {
            if (string.IsNullOrWhiteSpace(command.CompletionFingerprintSha256))
            {
                return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(
                    Error.Failure(
                        "agent_attempts.review_requires_completion_fingerprint",
                        "An Accepted or Challenged outcome requires fresh completion evidence confirming the checkpoint is still current."));
            }

            var reviewValidationError = ValidateReview(command.Outcome, command.Review);
            if (reviewValidationError is not null)
            {
                return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(reviewValidationError);
            }
        }
        else if (command.Review is not null)
        {
            return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.review_requires_success_outcome",
                    "A validated critical review was supplied for an outcome other than Accepted or Challenged."));
        }

        if (command.ProviderSessionId is { Length: > MaxProviderSessionIdLength })
        {
            return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(
                Error.Failure("agent_attempts.provider_session_id_too_long", "The reported provider session identifier exceeds its bound."));
        }

        var seenArtifactPurposes = new HashSet<ArtifactPurpose>();
        foreach (var sealedArtifact in command.SealedArtifacts)
        {
            if (!SupportedResultArtifactPurposes.Contains(sealedArtifact.Purpose))
            {
                return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(
                    Error.Conflict(
                        "agent_attempts.unsupported_artifact_purpose", "Only Agent output/final-response artifacts are supported here."));
            }

            if (!seenArtifactPurposes.Add(sealedArtifact.Purpose))
            {
                return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(
                    Error.Conflict("agent_attempts.duplicate_artifact_purpose", "The same artifact purpose was reported more than once."));
            }

            if (string.IsNullOrWhiteSpace(sealedArtifact.RelativeStoragePath)
                || string.IsNullOrWhiteSpace(sealedArtifact.ContentHash)
                || sealedArtifact.ByteLength < 0)
            {
                return Result<RecordClaudeCriticalReviewResultCommandResult>.Failure(
                    Error.Failure("agent_attempts.invalid_artifact_metadata", "One of the reported artifacts has malformed metadata."));
            }
        }

        var nowUtc = timeProvider.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(command.ProviderSessionId))
        {
            attempt.RecordAgentProviderSessionId(command.ProviderSessionId);
        }

        attempt.CompleteAgent(command.Outcome, command.CompletionFingerprintSha256, nowUtc);

        foreach (var sealedArtifact in command.SealedArtifacts)
        {
            RecordArtifact(command.RunId, command.AttemptId, sealedArtifact, nowUtc);
        }

        // The override inside CompleteAgent is the single source of truth for whether this
        // attempt actually completed as Accepted/Challenged — never the caller's pre-override
        // intent, so a review is never appended for an attempt that source-drift silently
        // downgraded.
        var inputMessageId = await dbContext.AttemptInputMessages
            .Where(inputMessage => inputMessage.AttemptId == attempt.Id && inputMessage.Sequence == 0)
            .Select(inputMessage => inputMessage.CollaborationMessageId)
            .SingleAsync(cancellationToken);
        RunEvent latestEvent;
        if (attempt.AgentOutcome == AgentOutcome.Accepted && command.Review is { IsAcceptance: true } acceptedReview)
        {
            latestEvent = RecordCollaborationMessage(
                attempt, inputMessageId, CollaborationMessageType.Acceptance,
                acceptedReview.Acceptance!.Summary, acceptedReview.Acceptance!.StructuredContentJson, nowUtc);
        }
        else if (attempt.AgentOutcome == AgentOutcome.Challenged && command.Review is { IsAcceptance: false } challengedReview)
        {
            latestEvent = null!;
            foreach (var challenge in challengedReview.Challenges)
            {
                latestEvent = RecordCollaborationMessage(
                    attempt, inputMessageId, CollaborationMessageType.Challenge,
                    challenge.Summary, challenge.StructuredContentJson, nowUtc);
            }
        }
        else
        {
            latestEvent = RunEvent.Record(
                Guid.NewGuid(),
                run.Id,
                attempt.Id,
                RunEventType.AgentAttemptCompleted,
                ParticipantIdentity.ForOrchestrator(),
                JsonSerializer.Serialize(new { status = attempt.Status.ToString(), outcome = attempt.AgentOutcome!.Value.ToString() }),
                nowUtc);
            dbContext.Events.Add(latestEvent);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<RecordClaudeCriticalReviewResultCommandResult>.Success(
            new RecordClaudeCriticalReviewResultCommandResult(attempt.Status, latestEvent.Sequence));
    }

    private static Error? ValidateReview(AgentOutcome outcome, ValidatedCriticalReview? review)
    {
        if (review is null)
        {
            return Error.Failure("agent_attempts.review_requires_validated_review", "An Accepted or Challenged outcome requires a validated review.");
        }

        if (outcome == AgentOutcome.Accepted)
        {
            if (!review.IsAcceptance || review.Acceptance is null)
            {
                return Error.Failure("agent_attempts.invalid_review_shape", "An Accepted outcome requires an Acceptance review.");
            }

            if (!CollaborationMessageContentPolicy.IsSafeSummary(review.Acceptance.Summary)
                || !TryValidateContent(CollaborationMessageType.Acceptance, review.Acceptance.StructuredContentJson))
            {
                return Error.Failure("agent_attempts.invalid_review_content", "The acceptance content failed content policy validation.");
            }

            return null;
        }

        // outcome == AgentOutcome.Challenged
        if (review.IsAcceptance || review.Challenges.Count is < 1 or > 5)
        {
            return Error.Failure("agent_attempts.invalid_review_shape", "A Challenged outcome requires one to five challenges.");
        }

        foreach (var challenge in review.Challenges)
        {
            if (!CollaborationMessageContentPolicy.IsSafeSummary(challenge.Summary)
                || !TryValidateContent(CollaborationMessageType.Challenge, challenge.StructuredContentJson))
            {
                return Error.Failure("agent_attempts.invalid_review_content", "One of the challenges failed content policy validation.");
            }
        }

        return null;
    }

    private static bool TryValidateContent(CollaborationMessageType type, string structuredContentJson)
    {
        try
        {
            CollaborationMessageContentPolicy.Validate(type, structuredContentJson);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private RunEvent RecordCollaborationMessage(
        Attempt attempt,
        Guid inReplyToMessageId,
        CollaborationMessageType type,
        string summary,
        string structuredContentJson,
        DateTimeOffset nowUtc)
    {
        var message = CollaborationMessage.RecordAgent(
            attempt,
            Guid.NewGuid(),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            type,
            inReplyToMessageId,
            summary,
            structuredContentJson,
            nowUtc);
        dbContext.CollaborationMessages.Add(message);

        var runEvent = RunEvent.Record(
            Guid.NewGuid(),
            attempt.RunId,
            attempt.Id,
            RunEventType.CollaborationMessageRecorded,
            message.Actor,
            JsonSerializer.Serialize(new
            {
                messageId = message.Id,
                type = message.Type.ToString(),
                provenance = message.Provenance.ToString(),
            }),
            nowUtc);
        dbContext.Events.Add(runEvent);

        return runEvent;
    }

    private void RecordArtifact(Guid runId, Guid attemptId, SealedCriticalReviewArtifact sealedArtifact, DateTimeOffset nowUtc)
    {
        var mediaType = sealedArtifact.Purpose == ArtifactPurpose.AgentFinalResponse
            ? "application/json"
            : "text/plain; charset=utf-8";

        var artifact = Artifact.Record(
            Guid.NewGuid(),
            runId,
            attemptId,
            sealedArtifact.Purpose,
            mediaType,
            sealedArtifact.RelativeStoragePath,
            sealedArtifact.ContentHash,
            sealedArtifact.ByteLength,
            sealedArtifact.Truncated,
            ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.RedactedBestEffort,
            ArtifactRetentionPolicy.RetainUntilRunDeleted,
            nowUtc);
        dbContext.Artifacts.Add(artifact);
    }
}
