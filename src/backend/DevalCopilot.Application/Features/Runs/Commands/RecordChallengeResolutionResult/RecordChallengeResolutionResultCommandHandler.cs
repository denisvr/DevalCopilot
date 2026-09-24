using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;

public sealed class RecordChallengeResolutionResultCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordChallengeResolutionResultCommand, Result<RecordChallengeResolutionResultCommandResult>>
{
    private static readonly IReadOnlySet<ArtifactPurpose> SupportedResultArtifactPurposes = new HashSet<ArtifactPurpose>
    {
        ArtifactPurpose.AgentStandardOutput,
        ArtifactPurpose.AgentStandardError,
        ArtifactPurpose.AgentFinalResponse,
    };

    /// <summary>
    /// The explicit, closed policy for what a Codex challenge-resolution attempt may report
    /// directly through this command. <see cref="AgentOutcome.SourceChanged"/> and
    /// <see cref="AgentOutcome.WorkspaceNoLongerEligible"/> are derived exclusively by their own
    /// dedicated pre-dispatch commands; <see cref="AgentOutcome.InputAlreadyResolved"/> is
    /// derived exclusively by <c>RecordChallengeResolutionInputAlreadyResolvedCommand</c>, which
    /// independently re-verifies the competing resolution before ever recording it; every
    /// outcome belonging to a different Agent contract (<see cref="AgentOutcome.Proposed"/>,
    /// <see cref="AgentOutcome.Accepted"/>, <see cref="AgentOutcome.Challenged"/>,
    /// <see cref="AgentOutcome.InputAlreadyReviewed"/>) is rejected the same closed way. Mirrors
    /// <c>RecordClaudeCriticalReviewResultCommandHandler.CallerSelectableOutcomes</c> exactly.
    /// </summary>
    private static readonly IReadOnlySet<AgentOutcome> CallerSelectableOutcomes = new HashSet<AgentOutcome>
    {
        AgentOutcome.Resolved,
        AgentOutcome.InvalidStructuredOutput,
        AgentOutcome.ProviderInvocationFailed,
        AgentOutcome.CheckpointEvidenceUnavailable,
    };

    private const int MaxProviderSessionIdLength = 256;

    public async Task<Result<RecordChallengeResolutionResultCommandResult>> HandleAsync(
        RecordChallengeResolutionResultCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (run is null || attempt is null || attempt.RunId != run.Id)
        {
            return Result<RecordChallengeResolutionResultCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run or attempt was not found."));
        }

        if (attempt.Kind != AttemptKind.Agent || attempt.AgentResponseContract != AgentResponseContract.ChallengeResolution)
        {
            return Result<RecordChallengeResolutionResultCommandResult>.Failure(
                Error.Conflict("attempts.not_challenge_resolution", "The attempt is not a Codex challenge-resolution attempt."));
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<RecordChallengeResolutionResultCommandResult>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot record a result."));
        }

        // Everything from here through the artifact-shape check is pure validation: no mutation
        // happens until every one of these has passed, so any rejection leaves the attempt, its
        // artifacts, and the ledger completely untouched.
        if (!attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result<RecordChallengeResolutionResultCommandResult>.Failure(
                Error.Conflict(
                    "agent_attempts.not_dispatched",
                    "A provider result cannot be recorded for an attempt that was never dispatched."));
        }

        if (!Enum.IsDefined(command.Outcome))
        {
            return Result<RecordChallengeResolutionResultCommandResult>.Failure(
                Error.Failure("agent_attempts.invalid_outcome", "The reported outcome is not a defined agent outcome."));
        }

        if (!CallerSelectableOutcomes.Contains(command.Outcome))
        {
            return Result<RecordChallengeResolutionResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.outcome_not_caller_selectable",
                    $"{command.Outcome} belongs to a dedicated host-derived recording path and cannot be reported directly here."));
        }

        // Sequence 0 is always the original Proposal; every remaining sequence is a Challenge in
        // timeline order. Loaded before the Resolved-specific checks below so it can also be used
        // to independently re-verify the caller-supplied Resolution actually resolves the exact
        // Challenge set this attempt was claimed against.
        var orderedInputMessages = await dbContext.AttemptInputMessages
            .Where(inputMessage => inputMessage.AttemptId == attempt.Id)
            .OrderBy(inputMessage => inputMessage.Sequence)
            .Select(inputMessage => new { inputMessage.Sequence, inputMessage.CollaborationMessageId })
            .ToListAsync(cancellationToken);
        var originalProposalMessageId = orderedInputMessages.Single(inputMessage => inputMessage.Sequence == 0).CollaborationMessageId;
        var expectedChallengeMessageIds = orderedInputMessages
            .Where(inputMessage => inputMessage.Sequence > 0)
            .Select(inputMessage => inputMessage.CollaborationMessageId)
            .ToHashSet();

        if (command.Outcome == AgentOutcome.Resolved)
        {
            if (string.IsNullOrWhiteSpace(command.CompletionFingerprintSha256))
            {
                return Result<RecordChallengeResolutionResultCommandResult>.Failure(
                    Error.Failure(
                        "agent_attempts.resolution_requires_completion_fingerprint",
                        "A Resolved outcome requires fresh completion evidence confirming the checkpoint is still current."));
            }

            var resolutionValidationError = ValidateResolution(command.Resolution, expectedChallengeMessageIds);
            if (resolutionValidationError is not null)
            {
                return Result<RecordChallengeResolutionResultCommandResult>.Failure(resolutionValidationError);
            }
        }
        else if (command.Resolution is not null)
        {
            return Result<RecordChallengeResolutionResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.resolution_requires_success_outcome",
                    "A validated challenge resolution was supplied for an outcome other than Resolved."));
        }

        // Host-measured process evidence is validated against the caller-requested outcome before
        // any mutation: bound to a dispatched attempt, and required as a clean exit for a semantic
        // success or InvalidStructuredOutput. Recorded atomically by CompleteAgent below.
        var processEvidenceError = AgentProcessEvidenceRecording.Validate(
            command.ProcessEvidence, command.Outcome, attempt.AgentDispatchedAtUtc.HasValue, out var processEvidence);
        if (processEvidenceError is not null)
        {
            return Result<RecordChallengeResolutionResultCommandResult>.Failure(processEvidenceError);
        }

        // Provider-reported token usage is best-effort and never required, but when supplied it is
        // validated before any mutation: bound to a dispatched attempt and never attached to a
        // pre-invocation outcome. Recorded atomically with the outcome and process evidence below.
        var tokenUsageError = AgentTokenUsageRecording.Validate(
            command.TokenUsage, attempt.AgentProvider, command.Outcome, attempt.AgentDispatchedAtUtc.HasValue, out var tokenUsage);
        if (tokenUsageError is not null)
        {
            return Result<RecordChallengeResolutionResultCommandResult>.Failure(tokenUsageError);
        }

        if (command.ProviderSessionId is { Length: > MaxProviderSessionIdLength })
        {
            return Result<RecordChallengeResolutionResultCommandResult>.Failure(
                Error.Failure("agent_attempts.provider_session_id_too_long", "The reported provider session identifier exceeds its bound."));
        }

        var seenArtifactPurposes = new HashSet<ArtifactPurpose>();
        foreach (var sealedArtifact in command.SealedArtifacts)
        {
            if (!SupportedResultArtifactPurposes.Contains(sealedArtifact.Purpose))
            {
                return Result<RecordChallengeResolutionResultCommandResult>.Failure(
                    Error.Conflict(
                        "agent_attempts.unsupported_artifact_purpose", "Only Agent output/final-response artifacts are supported here."));
            }

            if (!seenArtifactPurposes.Add(sealedArtifact.Purpose))
            {
                return Result<RecordChallengeResolutionResultCommandResult>.Failure(
                    Error.Conflict("agent_attempts.duplicate_artifact_purpose", "The same artifact purpose was reported more than once."));
            }

            if (string.IsNullOrWhiteSpace(sealedArtifact.RelativeStoragePath)
                || string.IsNullOrWhiteSpace(sealedArtifact.ContentHash)
                || sealedArtifact.ByteLength < 0)
            {
                return Result<RecordChallengeResolutionResultCommandResult>.Failure(
                    Error.Failure("agent_attempts.invalid_artifact_metadata", "One of the reported artifacts has malformed metadata."));
            }
        }

        var nowUtc = timeProvider.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(command.ProviderSessionId))
        {
            attempt.RecordAgentProviderSessionId(command.ProviderSessionId);
        }

        attempt.CompleteAgent(command.Outcome, command.CompletionFingerprintSha256, nowUtc, processEvidence, tokenUsage);

        foreach (var sealedArtifact in command.SealedArtifacts)
        {
            RecordArtifact(command.RunId, command.AttemptId, sealedArtifact, nowUtc);
        }

        // The override inside CompleteAgent is the single source of truth for whether this
        // attempt actually completed as Resolved — never the caller's pre-override intent, so a
        // resolution is never appended for an attempt that source-drift silently downgraded.
        RunEvent latestEvent;
        if (attempt.AgentOutcome == AgentOutcome.Resolved && command.Resolution is { } resolution)
        {
            latestEvent = null!;
            foreach (var decision in resolution.Decisions)
            {
                latestEvent = RecordCollaborationMessage(
                    attempt, decision.ChallengeMessageId, CollaborationMessageType.Decision,
                    decision.Summary, decision.StructuredContentJson, nowUtc);
            }

            latestEvent = RecordCollaborationMessage(
                attempt, originalProposalMessageId, CollaborationMessageType.Proposal,
                resolution.RevisedProposal.Summary, resolution.RevisedProposal.StructuredContentJson, nowUtc);
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

        return Result<RecordChallengeResolutionResultCommandResult>.Success(
            new RecordChallengeResolutionResultCommandResult(attempt.Status, latestEvent.Sequence));
    }

    private static Error? ValidateResolution(ValidatedChallengeResolution? resolution, IReadOnlySet<Guid> expectedChallengeMessageIds)
    {
        if (resolution is null)
        {
            return Error.Failure(
                "agent_attempts.resolution_requires_validated_resolution", "A Resolved outcome requires a validated resolution.");
        }

        // The parser that produced this ValidatedChallengeResolution was itself supplied the
        // expected Challenge set, so this is deliberate defense in depth against a stale or
        // mismatched value ever reaching this handler — never the only check, and never reliant
        // on the parser alone. Count and uniqueness are checked explicitly, before ever reducing
        // to a set: collapsing straight to a HashSet first (as a naive SetEquals check would)
        // silently swallows a duplicated Challenge id — e.g. two Decisions for the same
        // Challenge plus one missing entirely can still produce a set that matches the expected
        // one by pure coincidence of cardinality, letting an under- or over-resolved response
        // through and appending two Decision messages in reply to a single Challenge.
        if (resolution.Decisions.Count != expectedChallengeMessageIds.Count)
        {
            return Error.Failure(
                "agent_attempts.invalid_resolution_shape",
                "The reported resolution does not resolve exactly the attempt's claimed Challenge set.");
        }

        var resolvedChallengeMessageIds = new HashSet<Guid>();
        foreach (var decision in resolution.Decisions)
        {
            if (!resolvedChallengeMessageIds.Add(decision.ChallengeMessageId))
            {
                return Error.Failure(
                    "agent_attempts.invalid_resolution_shape",
                    "The reported resolution resolves the same Challenge more than once.");
            }
        }

        if (!resolvedChallengeMessageIds.SetEquals(expectedChallengeMessageIds))
        {
            return Error.Failure(
                "agent_attempts.invalid_resolution_shape",
                "The reported resolution does not resolve exactly the attempt's claimed Challenge set.");
        }

        foreach (var decision in resolution.Decisions)
        {
            if (!CollaborationMessageContentPolicy.IsSafeSummary(decision.Summary)
                || !TryValidateContent(CollaborationMessageType.Decision, decision.StructuredContentJson))
            {
                return Error.Failure("agent_attempts.invalid_resolution_content", "One of the decisions failed content policy validation.");
            }
        }

        if (!CollaborationMessageContentPolicy.IsSafeSummary(resolution.RevisedProposal.Summary)
            || !TryValidateContent(CollaborationMessageType.Proposal, resolution.RevisedProposal.StructuredContentJson))
        {
            return Error.Failure("agent_attempts.invalid_resolution_content", "The revised proposal failed content policy validation.");
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
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
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

    private void RecordArtifact(Guid runId, Guid attemptId, SealedChallengeResolutionArtifact sealedArtifact, DateTimeOffset nowUtc)
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
