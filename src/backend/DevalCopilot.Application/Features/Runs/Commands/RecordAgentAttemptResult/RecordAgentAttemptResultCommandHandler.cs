using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;

public sealed class RecordAgentAttemptResultCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordAgentAttemptResultCommand, Result<RecordAgentAttemptResultCommandResult>>
{
    private static readonly IReadOnlySet<ArtifactPurpose> SupportedResultArtifactPurposes = new HashSet<ArtifactPurpose>
    {
        ArtifactPurpose.AgentStandardOutput,
        ArtifactPurpose.AgentStandardError,
        ArtifactPurpose.AgentFinalResponse,
    };

    /// <summary>
    /// The explicit, closed policy for what a Codex planning attempt may report directly through
    /// this command. <see cref="AgentOutcome.SourceChanged"/> and
    /// <see cref="AgentOutcome.WorkspaceNoLongerEligible"/> are derived exclusively by their own
    /// dedicated pre-dispatch commands; <see cref="AgentOutcome.InputAlreadyReviewed"/> and the
    /// critical-review success outcomes (<see cref="AgentOutcome.Accepted"/>,
    /// <see cref="AgentOutcome.Challenged"/>) belong to the Claude critical-review contract, never
    /// this one. Checking membership in this set — rather than singling out one excluded value at
    /// a time — is what makes the rejection exhaustive: every outcome this handler does not
    /// explicitly allow is rejected the same safe way, with zero mutation, before
    /// <see cref="Attempt.CompleteAgent"/> is ever called. That call remains an independent
    /// Domain-level backstop, never the only line of defense.
    /// </summary>
    private static readonly IReadOnlySet<AgentOutcome> CallerSelectableOutcomes = new HashSet<AgentOutcome>
    {
        AgentOutcome.Proposed,
        AgentOutcome.InvalidStructuredOutput,
        AgentOutcome.ProviderInvocationFailed,
        AgentOutcome.CheckpointEvidenceUnavailable,
    };

    /// <summary>Matches both the EF column bound and the adapter's own scan bound — never
    /// silently truncated or left to fail unpredictably at save time.</summary>
    private const int MaxProviderSessionIdLength = 256;

    public async Task<Result<RecordAgentAttemptResultCommandResult>> HandleAsync(
        RecordAgentAttemptResultCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);

        if (run is null || attempt is null || attempt.RunId != run.Id)
        {
            return Result<RecordAgentAttemptResultCommandResult>.Failure(
                Error.NotFound("runs.not_found", "The requested run or attempt was not found."));
        }

        if (attempt.Kind != AttemptKind.Agent)
        {
            return Result<RecordAgentAttemptResultCommandResult>.Failure(
                Error.Conflict("attempts.not_agent", "The attempt is not an Agent attempt."));
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Result<RecordAgentAttemptResultCommandResult>.Failure(
                Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot record a result."));
        }

        // Everything from here through the artifact-shape check is pure validation: no mutation
        // happens until every one of these has passed, so any rejection leaves the attempt, its
        // artifacts, and the ledger completely untouched — never a partial mutation followed by a
        // thrown exception.
        if (!attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Result<RecordAgentAttemptResultCommandResult>.Failure(
                Error.Conflict(
                    "agent_attempts.not_dispatched",
                    "A provider result cannot be recorded for an attempt that was never dispatched."));
        }

        if (!Enum.IsDefined(command.Outcome))
        {
            return Result<RecordAgentAttemptResultCommandResult>.Failure(
                Error.Failure("agent_attempts.invalid_outcome", "The reported outcome is not a defined agent outcome."));
        }

        if (!CallerSelectableOutcomes.Contains(command.Outcome))
        {
            return Result<RecordAgentAttemptResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.outcome_not_caller_selectable",
                    $"{command.Outcome} belongs to a dedicated host-derived recording path and cannot be reported directly here."));
        }

        if (command.Outcome == AgentOutcome.Proposed)
        {
            if (string.IsNullOrWhiteSpace(command.CompletionFingerprintSha256))
            {
                return Result<RecordAgentAttemptResultCommandResult>.Failure(
                    Error.Failure(
                        "agent_attempts.proposed_requires_completion_fingerprint",
                        "A Proposed outcome requires fresh completion evidence confirming the checkpoint is still current."));
            }

            if (command.Proposal is null)
            {
                return Result<RecordAgentAttemptResultCommandResult>.Failure(
                    Error.Failure("agent_attempts.proposed_requires_proposal", "A Proposed outcome requires a validated proposal."));
            }

            if (!CollaborationMessageContentPolicy.IsSafeSummary(command.Proposal.Summary))
            {
                return Result<RecordAgentAttemptResultCommandResult>.Failure(
                    Error.Failure("agent_attempts.invalid_proposal_content", "The proposal summary failed content policy validation."));
            }

            try
            {
                CollaborationMessageContentPolicy.Validate(CollaborationMessageType.Proposal, command.Proposal.StructuredContentJson);
            }
            catch (ArgumentException)
            {
                return Result<RecordAgentAttemptResultCommandResult>.Failure(
                    Error.Failure(
                        "agent_attempts.invalid_proposal_content", "The proposal's structured content failed content policy validation."));
            }
        }
        else if (command.Proposal is not null)
        {
            return Result<RecordAgentAttemptResultCommandResult>.Failure(
                Error.Failure(
                    "agent_attempts.proposal_requires_proposed_outcome",
                    "A validated proposal was supplied for an outcome other than Proposed."));
        }

        if (command.ProviderSessionId is { Length: > MaxProviderSessionIdLength })
        {
            return Result<RecordAgentAttemptResultCommandResult>.Failure(
                Error.Failure("agent_attempts.provider_session_id_too_long", "The reported provider session identifier exceeds its bound."));
        }

        var seenArtifactPurposes = new HashSet<ArtifactPurpose>();
        foreach (var sealedArtifact in command.SealedArtifacts)
        {
            if (!SupportedResultArtifactPurposes.Contains(sealedArtifact.Purpose))
            {
                return Result<RecordAgentAttemptResultCommandResult>.Failure(
                    Error.Conflict(
                        "agent_attempts.unsupported_artifact_purpose", "Only Agent output/final-response artifacts are supported here."));
            }

            if (!seenArtifactPurposes.Add(sealedArtifact.Purpose))
            {
                return Result<RecordAgentAttemptResultCommandResult>.Failure(
                    Error.Conflict("agent_attempts.duplicate_artifact_purpose", "The same artifact purpose was reported more than once."));
            }

            if (string.IsNullOrWhiteSpace(sealedArtifact.RelativeStoragePath)
                || string.IsNullOrWhiteSpace(sealedArtifact.ContentHash)
                || sealedArtifact.ByteLength < 0)
            {
                return Result<RecordAgentAttemptResultCommandResult>.Failure(
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
        // attempt actually completed as a Proposal — never the caller's pre-override intent, so
        // a Proposal is never appended for an attempt that source-drift silently downgraded.
        RunEvent latestEvent;
        if (attempt.AgentOutcome == AgentOutcome.Proposed && command.Proposal is { } proposal)
        {
            var message = CollaborationMessage.Record(
                Guid.NewGuid(),
                run.Id,
                attempt.Id,
                CollaborationMessage.ProtocolVersionOne,
                ParticipantKind.Codex,
                ParticipantKind.Claude,
                CollaborationMessageType.Proposal,
                null,
                proposal.Summary,
                proposal.StructuredContentJson,
                CollaborationMessageProvenance.ProviderObserved,
                nowUtc);
            dbContext.CollaborationMessages.Add(message);

            latestEvent = RunEvent.Record(
                Guid.NewGuid(),
                run.Id,
                attempt.Id,
                RunEventType.CollaborationMessageRecorded,
                ParticipantKind.Codex,
                JsonSerializer.Serialize(new
                {
                    messageId = message.Id,
                    type = message.Type.ToString(),
                    provenance = message.Provenance.ToString(),
                }),
                nowUtc);
        }
        else
        {
            latestEvent = RunEvent.Record(
                Guid.NewGuid(),
                run.Id,
                attempt.Id,
                RunEventType.AgentAttemptCompleted,
                ParticipantKind.Orchestrator,
                JsonSerializer.Serialize(new { status = attempt.Status.ToString(), outcome = attempt.AgentOutcome!.Value.ToString() }),
                nowUtc);
        }

        dbContext.Events.Add(latestEvent);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<RecordAgentAttemptResultCommandResult>.Success(
            new RecordAgentAttemptResultCommandResult(attempt.Status, latestEvent.Sequence));
    }

    private void RecordArtifact(Guid runId, Guid attemptId, SealedAgentArtifact sealedArtifact, DateTimeOffset nowUtc)
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
