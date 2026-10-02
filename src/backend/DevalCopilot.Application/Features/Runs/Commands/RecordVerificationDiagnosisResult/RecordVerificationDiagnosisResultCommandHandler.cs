using System.Text.Json;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Domain.Features.Runs;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordVerificationDiagnosisResult;

public sealed class RecordVerificationDiagnosisResultCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordVerificationDiagnosisResultCommand, Result<RecordVerificationDiagnosisResultCommandResult>>
{
    private static readonly IReadOnlySet<ArtifactPurpose> SupportedResultArtifactPurposes = new HashSet<ArtifactPurpose>
    {
        ArtifactPurpose.AgentStandardOutput,
        ArtifactPurpose.AgentStandardError,
        ArtifactPurpose.AgentFinalResponse,
    };

    /// <summary>
    /// The explicit, closed policy for what a verification-diagnosis attempt may report directly through this command.
    /// <see cref="AgentOutcome.SourceChanged"/> and <see cref="AgentOutcome.WorkspaceNoLongerEligible"/> are derived by their
    /// own pre-dispatch commands, <see cref="AgentOutcome.InputAlreadyDiagnosed"/> and the pre-dispatch
    /// <see cref="AgentOutcome.VerificationEvidenceChanged"/> by the dedicated dispatch-refusal command, and a post-process
    /// <see cref="AgentOutcome.VerificationEvidenceChanged"/> only by this handler's own fresh applicability check.
    /// </summary>
    private static readonly IReadOnlySet<AgentOutcome> CallerSelectableOutcomes = new HashSet<AgentOutcome>
    {
        AgentOutcome.DiagnosisFindingsRecorded,
        AgentOutcome.DiagnosisEscalated,
        AgentOutcome.InvalidStructuredOutput,
        AgentOutcome.ProviderInvocationFailed,
        AgentOutcome.CheckpointEvidenceUnavailable,
    };

    private const int MaxProviderSessionIdLength = 256;

    public async Task<Result<RecordVerificationDiagnosisResultCommandResult>> HandleAsync(
        RecordVerificationDiagnosisResultCommand command, CancellationToken cancellationToken)
    {
        var run = await dbContext.Runs.SingleOrDefaultAsync(candidate => candidate.Id == command.RunId, cancellationToken);
        var attempt = await dbContext.Attempts.SingleOrDefaultAsync(candidate => candidate.Id == command.AttemptId, cancellationToken);
        if (run is null || attempt is null || attempt.RunId != run.Id)
        {
            return Failure(Error.NotFound("runs.not_found", "The requested run or attempt was not found."));
        }

        if (attempt.Kind != AttemptKind.Agent || attempt.AgentResponseContract != AgentResponseContract.VerificationDiagnosis)
        {
            return Failure(Error.Conflict("attempts.not_verification_diagnosis", "The attempt is not a verification-diagnosis attempt."));
        }

        if (attempt.Status != AttemptStatus.Running)
        {
            return Failure(Error.Conflict("attempts.not_active", $"The attempt is {attempt.Status} and cannot record a result."));
        }

        if (!attempt.AgentDispatchedAtUtc.HasValue)
        {
            return Failure(Error.Conflict(
                "agent_attempts.not_dispatched", "A provider result cannot be recorded for an attempt that was never dispatched."));
        }

        if (!Enum.IsDefined(command.Outcome))
        {
            return Failure(Error.Failure("agent_attempts.invalid_outcome", "The reported outcome is not a defined agent outcome."));
        }

        if (!CallerSelectableOutcomes.Contains(command.Outcome))
        {
            return Failure(Error.Failure(
                "agent_attempts.outcome_not_caller_selectable",
                $"{command.Outcome} belongs to a dedicated host-derived recording path and cannot be reported directly here."));
        }

        var reportMessageId = await VerificationDiagnosisInputIdentity.ReadPinnedExecutionReportIdAsync(dbContext, attempt.Id, cancellationToken);
        if (reportMessageId is null)
        {
            return Failure(Error.Conflict("agent_attempts.diagnosis_input_invalid", "The diagnosis input identity is not valid."));
        }

        var isSemantic = command.Outcome is AgentOutcome.DiagnosisFindingsRecorded or AgentOutcome.DiagnosisEscalated;
        if (isSemantic)
        {
            if (string.IsNullOrWhiteSpace(command.CompletionFingerprintSha256))
            {
                return Failure(Error.Failure(
                    "agent_attempts.diagnosis_requires_completion_fingerprint",
                    "A diagnosis outcome requires fresh completion evidence confirming the checkpoint is still current."));
            }

            var shapeError = ValidateDiagnosis(command.Outcome, command.Diagnosis);
            if (shapeError is not null)
            {
                return Failure(shapeError);
            }
        }
        else if (command.Diagnosis is not null)
        {
            return Failure(Error.Failure(
                "agent_attempts.diagnosis_requires_diagnosis_outcome",
                "A validated diagnosis was supplied for an outcome other than DiagnosisFindingsRecorded or DiagnosisEscalated."));
        }

        // The requested outcome's own process proof is validated first, so drift can only downgrade a result that was acceptable as
        // requested: missing, nonzero, timed-out or cancelled evidence is never accepted through reclassification.
        var requestedEvidenceError = AgentProcessEvidenceRecording.Validate(
            command.ProcessEvidence, command.Outcome, attempt.AgentDispatchedAtUtc.HasValue, out _);
        if (requestedEvidenceError is not null)
        {
            return Failure(requestedEvidenceError);
        }

        // Fresh, untracked applicability, decided before the result is recorded: a response produced against verification
        // evidence, a report chain, or a workspace that no longer holds is never turned into a finding or escalation. The
        // truthful process and artifact evidence below is retained either way.
        var effectiveOutcome = command.Outcome;
        if (isSemantic
            && await VerificationDiagnosisApplicability.EvaluateAsync(dbContext, attempt, cancellationToken)
                != VerificationDiagnosisApplicability.Verdict.Applicable)
        {
            effectiveOutcome = AgentOutcome.VerificationEvidenceChanged;
        }

        var processEvidenceError = AgentProcessEvidenceRecording.Validate(
            command.ProcessEvidence, effectiveOutcome, attempt.AgentDispatchedAtUtc.HasValue, out var processEvidence);
        if (processEvidenceError is not null)
        {
            return Failure(processEvidenceError);
        }

        var tokenUsageError = AgentTokenUsageRecording.Validate(
            command.TokenUsage, attempt.AgentProvider, effectiveOutcome, attempt.AgentDispatchedAtUtc.HasValue, out var tokenUsage);
        if (tokenUsageError is not null)
        {
            return Failure(tokenUsageError);
        }

        if (command.ProviderSessionId is { Length: > MaxProviderSessionIdLength })
        {
            return Failure(Error.Failure("agent_attempts.provider_session_id_too_long", "The reported provider session identifier exceeds its bound."));
        }

        var seenArtifactPurposes = new HashSet<ArtifactPurpose>();
        foreach (var sealedArtifact in command.SealedArtifacts)
        {
            if (!SupportedResultArtifactPurposes.Contains(sealedArtifact.Purpose))
            {
                return Failure(Error.Conflict(
                    "agent_attempts.unsupported_artifact_purpose", "Only Agent output/final-response artifacts are supported here."));
            }

            if (!seenArtifactPurposes.Add(sealedArtifact.Purpose))
            {
                return Failure(Error.Conflict("agent_attempts.duplicate_artifact_purpose", "The same artifact purpose was reported more than once."));
            }

            if (string.IsNullOrWhiteSpace(sealedArtifact.RelativeStoragePath)
                || string.IsNullOrWhiteSpace(sealedArtifact.ContentHash)
                || sealedArtifact.ByteLength < 0)
            {
                return Failure(Error.Failure("agent_attempts.invalid_artifact_metadata", "One of the reported artifacts has malformed metadata."));
            }
        }

        var nowUtc = timeProvider.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(command.ProviderSessionId))
        {
            attempt.RecordAgentProviderSessionId(command.ProviderSessionId);
        }

        // CompleteAgent's own fingerprint-mismatch-to-SourceChanged override is authoritative and stays the Git-source
        // classification; the check below is against the attempt's post-override outcome, never the caller's intent.
        attempt.CompleteAgent(effectiveOutcome, command.CompletionFingerprintSha256, nowUtc, processEvidence, tokenUsage);

        foreach (var sealedArtifact in command.SealedArtifacts)
        {
            RecordArtifact(command.RunId, command.AttemptId, sealedArtifact, nowUtc);
        }

        RunEvent latestEvent;
        if (attempt.AgentOutcome == AgentOutcome.DiagnosisFindingsRecorded && command.Diagnosis is { IsEscalation: false } findingsDiagnosis)
        {
            latestEvent = null!;
            foreach (var finding in findingsDiagnosis.Findings)
            {
                latestEvent = RecordMessage(
                    attempt,
                    reportMessageId.Value,
                    ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                    CollaborationMessageType.ReviewFinding,
                    finding.Summary,
                    VerificationDiagnosisResponseParser.SerializeFinding(finding),
                    nowUtc);
            }
        }
        else if (attempt.AgentOutcome == AgentOutcome.DiagnosisEscalated && command.Diagnosis is { Escalation: { } escalation } escalatedDiagnosis)
        {
            latestEvent = RecordMessage(
                attempt,
                reportMessageId.Value,
                ParticipantIdentity.ForHuman(),
                CollaborationMessageType.Escalation,
                escalatedDiagnosis.Summary,
                VerificationDiagnosisResponseParser.SerializeEscalation(escalation),
                nowUtc);
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

        return Result<RecordVerificationDiagnosisResultCommandResult>.Success(
            new RecordVerificationDiagnosisResultCommandResult(attempt.Status, attempt.AgentOutcome!.Value, latestEvent.Sequence));
    }

    private static Error? ValidateDiagnosis(AgentOutcome outcome, ValidatedVerificationDiagnosis? diagnosis)
    {
        if (diagnosis is null)
        {
            return Error.Failure("agent_attempts.diagnosis_requires_validated_diagnosis", "A diagnosis outcome requires a validated diagnosis.");
        }

        if (!CollaborationMessageContentPolicy.IsSafeSummary(diagnosis.Summary))
        {
            return Error.Failure("agent_attempts.invalid_diagnosis_content", "The diagnosis summary failed content policy validation.");
        }

        if (outcome == AgentOutcome.DiagnosisEscalated)
        {
            if (diagnosis.Escalation is not { } escalation || diagnosis.Findings.Count != 0)
            {
                return Error.Failure("agent_attempts.invalid_diagnosis_shape", "A DiagnosisEscalated outcome requires exactly one escalation and no findings.");
            }

            return TryValidateContent(CollaborationMessageType.Escalation, VerificationDiagnosisResponseParser.SerializeEscalation(escalation))
                ? null
                : Error.Failure("agent_attempts.invalid_diagnosis_content", "The escalation failed content policy validation.");
        }

        if (diagnosis.Escalation is not null
            || diagnosis.Findings.Count is < VerificationDiagnosisPolicy.MinimumFindings or > VerificationDiagnosisPolicy.MaximumFindings)
        {
            return Error.Failure("agent_attempts.invalid_diagnosis_shape", "A DiagnosisFindingsRecorded outcome requires one to ten findings and no escalation.");
        }

        foreach (var finding in diagnosis.Findings)
        {
            if (!CollaborationMessageContentPolicy.IsSafeSummary(finding.Summary)
                || !TryValidateContent(CollaborationMessageType.ReviewFinding, VerificationDiagnosisResponseParser.SerializeFinding(finding)))
            {
                return Error.Failure("agent_attempts.invalid_diagnosis_content", "One of the findings failed content policy validation.");
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

    private RunEvent RecordMessage(
        Attempt attempt,
        Guid inReplyToMessageId,
        ParticipantIdentity recipient,
        CollaborationMessageType type,
        string summary,
        string structuredContentJson,
        DateTimeOffset nowUtc)
    {
        var message = CollaborationMessage.RecordAgent(
            attempt, Guid.NewGuid(), recipient, type, inReplyToMessageId, summary, structuredContentJson, nowUtc);
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

    private void RecordArtifact(Guid runId, Guid attemptId, SealedVerificationDiagnosisArtifact sealedArtifact, DateTimeOffset nowUtc)
    {
        var mediaType = sealedArtifact.Purpose == ArtifactPurpose.AgentFinalResponse ? "application/json" : "text/plain; charset=utf-8";
        dbContext.Artifacts.Add(Artifact.Record(
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
            nowUtc));
    }

    private static Result<RecordVerificationDiagnosisResultCommandResult> Failure(Error error) =>
        Result<RecordVerificationDiagnosisResultCommandResult>.Failure(error);
}
