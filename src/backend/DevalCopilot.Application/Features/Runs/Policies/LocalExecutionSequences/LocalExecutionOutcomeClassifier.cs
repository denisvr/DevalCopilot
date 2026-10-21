using System.Text;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// Classifies the terminal result of one linked operation into the single next decision of the fixed progression (ADR-0060). It reads
/// only the operation's own durable rows and the exact recorded pass; it never selects the latest message or execution, never skips a
/// failed recipe and never reuses a previous pass. Any missing, foreign, ambiguous or unproved fact ends advancement as NeedsAttention
/// instead of entering diagnosis or another mutation.
/// </summary>
internal static class LocalExecutionOutcomeClassifier
{
    public static async Task<LocalExecutionVerdict> ClassifyAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        IReadOnlyList<LocalExecutionSequenceStep> steps,
        LocalExecutionSequenceStep claimed,
        Run run,
        CancellationToken cancellationToken)
    {
        if (claimed.OperationId is not { } operationId)
        {
            return LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationOutcomeInvalid);
        }

        return claimed.Kind switch
        {
            LocalExecutionStepKind.Verification => await ClassifyVerificationAsync(dbContext, sequence, claimed, operationId, cancellationToken),
            _ => await ClassifyAgentAsync(dbContext, sequence, steps, claimed, run, operationId, cancellationToken),
        };
    }

    private static async Task<LocalExecutionVerdict> ClassifyAgentAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        IReadOnlyList<LocalExecutionSequenceStep> steps,
        LocalExecutionSequenceStep claimed,
        Run run,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Attempts.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == attemptId, cancellationToken);
        if (attempt is null || attempt.RunId != sequence.RunId || attempt.Kind != AttemptKind.Agent)
        {
            return LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationOutcomeInvalid);
        }

        var detail = DetailOf(attempt.AgentOutcome?.ToString());
        if (attempt.Status != AttemptStatus.Completed)
        {
            // A dedicated undispatched refusal needs no process proof; a dispatched row without Exited proof stays quarantined.
            return LocalExecutionVerdict.Attention(
                attempt.AgentDispatchedAtUtc is not null && !LocalExecutionExitProof.Holds(attempt)
                    ? LocalExecutionSequenceReason.ExitEvidenceMissing
                    : LocalExecutionSequenceReason.OperationFailed,
                detail);
        }

        if (!LocalExecutionExitProof.Holds(attempt) || attempt.GetAgentProcessExecutionEvidence() is not { IsCleanExit: true })
        {
            return LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.ExitEvidenceMissing, detail);
        }

        switch (claimed.Kind)
        {
            case LocalExecutionStepKind.Implementation:
            case LocalExecutionStepKind.ReviewCorrection:
            case LocalExecutionStepKind.DiagnosisCorrection:
                {
                    var result = await LocalExecutionResultSource.TryDeriveAsync(dbContext, sequence, claimed, attempt, cancellationToken);
                    var nextPass = claimed.Kind == LocalExecutionStepKind.Implementation ? 1 : claimed.PassNumber + 1;
                    return result is null || nextPass > LocalExecutionSequencePolicy.MaximumPasses
                        ? LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationOutcomeInvalid, detail)
                        : LocalExecutionVerdict.Next(
                            LocalExecutionStepKind.Verification,
                            nextPass,
                            0,
                            result.CheckpointId,
                            result.FingerprintSha256,
                            result.ReportMessageId);
                }

            case LocalExecutionStepKind.CodeReview:
                return await ClassifyReviewAsync(dbContext, sequence, steps, claimed, run, attempt, cancellationToken);

            default:
                return await ClassifyDiagnosisAsync(dbContext, sequence, steps, claimed, run, attempt, cancellationToken);
        }
    }

    private static async Task<LocalExecutionVerdict> ClassifyReviewAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        IReadOnlyList<LocalExecutionSequenceStep> steps,
        LocalExecutionSequenceStep claimed,
        Run run,
        Attempt attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.AgentResponseContract != AgentResponseContract.ImplementationReview
            || attempt.AgentGitWorkspaceId != sequence.WorkspaceId
            || attempt.AgentGitCheckpointId != claimed.SourceCheckpointId)
        {
            return LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationOutcomeInvalid);
        }

        var reportId = claimed.ReportMessageId!.Value;
        if (attempt.AgentOutcome == AgentOutcome.ReviewApproved)
        {
            // The approval is proved from the actual ReviewApproval, its provenance, reply, inputs and the exact recorded pass.
            return await LocalExecutionFinalEvidence.ApprovalHoldsAsync(dbContext, sequence, claimed, attempt, cancellationToken)
                ? LocalExecutionVerdict.Approved(reportId, attempt.Id)
                : LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationOutcomeInvalid, DetailOf(nameof(AgentOutcome.ReviewApproved)));
        }

        return attempt.AgentOutcome == AgentOutcome.ReviewChangesRequested
            ? await CorrectionOrExhaustionAsync(dbContext, sequence, steps, claimed, run, attempt, LocalExecutionStepKind.ReviewCorrection, cancellationToken)
            : LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationFailed, DetailOf(attempt.AgentOutcome?.ToString()));
    }

    private static async Task<LocalExecutionVerdict> ClassifyDiagnosisAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        IReadOnlyList<LocalExecutionSequenceStep> steps,
        LocalExecutionSequenceStep claimed,
        Run run,
        Attempt attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.AgentResponseContract != AgentResponseContract.VerificationDiagnosis
            || attempt.AgentGitWorkspaceId != sequence.WorkspaceId
            || attempt.AgentGitCheckpointId != claimed.SourceCheckpointId)
        {
            return LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationOutcomeInvalid);
        }

        var reportId = claimed.ReportMessageId!.Value;
        if (attempt.AgentOutcome == AgentOutcome.DiagnosisEscalated)
        {
            var escalations = await dbContext.CollaborationMessages.AsNoTracking()
                .Where(message => message.AttemptId == attempt.Id
                    && message.Type == CollaborationMessageType.Escalation
                    && message.InReplyToMessageId == reportId
                    && message.Provenance == CollaborationMessageProvenance.ProviderObserved)
                .Select(message => message.Id)
                .ToListAsync(cancellationToken);
            return escalations.Count == 1
                && await LocalExecutionFinalEvidence.ProviderEscalationHoldsAsync(dbContext, sequence, claimed, attempt, escalations[0], cancellationToken)
                ? LocalExecutionVerdict.ProviderEscalation(reportId, escalations[0])
                : LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationOutcomeInvalid, DetailOf(nameof(AgentOutcome.DiagnosisEscalated)));
        }

        return attempt.AgentOutcome == AgentOutcome.DiagnosisFindingsRecorded
            ? await CorrectionOrExhaustionAsync(dbContext, sequence, steps, claimed, run, attempt, LocalExecutionStepKind.DiagnosisCorrection, cancellationToken)
            : LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationFailed, DetailOf(attempt.AgentOutcome?.ToString()));
    }

    /// <summary>Selects the matching correction origin for the complete finding set of the linked review or diagnosis, or the one fixed
    /// exhaustion decision. It never probes an ordinary claim: that would write an escalation or consume a Human grant.</summary>
    private static async Task<LocalExecutionVerdict> CorrectionOrExhaustionAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        IReadOnlyList<LocalExecutionSequenceStep> steps,
        LocalExecutionSequenceStep claimed,
        Run run,
        Attempt reviewOrDiagnosis,
        LocalExecutionStepKind correctionKind,
        CancellationToken cancellationToken)
    {
        var findings = await dbContext.CollaborationMessages.AsNoTracking().CountAsync(
            message => message.AttemptId == reviewOrDiagnosis.Id
                && message.Type == CollaborationMessageType.ReviewFinding
                && message.InReplyToMessageId == claimed.ReportMessageId
                && message.Provenance == CollaborationMessageProvenance.ProviderObserved,
            cancellationToken);
        if (findings is < 1 or > 10)
        {
            return LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationOutcomeInvalid);
        }

        var corrections = steps.Count(step => LocalExecutionSequencePolicy.IsCorrectionKind(step.Kind)
            && step.State != LocalExecutionSequenceStepState.Revoked);
        var ordinaryCorrections = await dbContext.Attempts.AsNoTracking().CountAsync(
            attempt => attempt.RunId == sequence.RunId && attempt.AgentResponseContract == AgentResponseContract.ReviewCorrection,
            cancellationToken);
        if (corrections >= sequence.CorrectionAllowance || ordinaryCorrections >= run.MaximumReviewCorrectionAttempts)
        {
            return LocalExecutionVerdict.AllowanceReached(claimed.ReportMessageId!.Value);
        }

        return LocalExecutionVerdict.Next(
            correctionKind,
            claimed.PassNumber,
            null,
            claimed.SourceCheckpointId,
            claimed.SourceFingerprintSha256,
            claimed.ReportMessageId!.Value,
            reviewOrDiagnosis.Id);
    }

    private static async Task<LocalExecutionVerdict> ClassifyVerificationAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        LocalExecutionSequenceStep claimed,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        var execution = await dbContext.VerificationExecutions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == executionId, cancellationToken);
        if (execution is null || execution.ProjectId != sequence.ProjectId)
        {
            return LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationOutcomeInvalid);
        }

        var detail = DetailOf(execution.Status.ToString());
        if (!LocalExecutionExitProof.Holds(execution))
        {
            return LocalExecutionVerdict.Attention(
                execution.DispatchedAtUtc is not null ? LocalExecutionSequenceReason.ExitEvidenceMissing : LocalExecutionSequenceReason.OperationFailed,
                detail);
        }

        // Only a coherent Passed (exit zero) or Failed (nonzero exit) result continues the pass; drift, timeout and every other status
        // closes advancement, and the whole pass is finished after an ordinary nonzero exit.
        if (!IsCoherent(execution, claimed))
        {
            return LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationOutcomeInvalid, detail);
        }

        var reportId = claimed.ReportMessageId!.Value;
        if (claimed.RecipeOrdinal!.Value + 1 < sequence.RecipeCount)
        {
            return LocalExecutionVerdict.Next(
                LocalExecutionStepKind.Verification,
                claimed.PassNumber,
                claimed.RecipeOrdinal.Value + 1,
                claimed.SourceCheckpointId,
                claimed.SourceFingerprintSha256,
                reportId);
        }

        var pass = await LocalExecutionStepAuthority.PassExecutionsAsync(dbContext, sequence.Id, claimed.PassNumber, cancellationToken);
        if (pass.Count != sequence.RecipeCount || pass.Select((member, index) => member.RecipeOrdinal == index).Any(same => !same))
        {
            return LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.OperationOutcomeInvalid);
        }

        var ids = pass.Select(member => member.ExecutionId).ToList();
        var executions = await dbContext.VerificationExecutions.AsNoTracking()
            .Where(candidate => ids.Contains(candidate.Id))
            .ToListAsync(cancellationToken);
        if (executions.Count != ids.Count || executions.Any(candidate => !IsCoherent(candidate, claimed)))
        {
            return LocalExecutionVerdict.Attention(LocalExecutionSequenceReason.VerificationNotDiagnosable);
        }

        var failed = executions.Count(candidate => candidate.Status == VerificationExecutionStatus.Failed);
        return LocalExecutionVerdict.Next(
            failed == 0 ? LocalExecutionStepKind.CodeReview : LocalExecutionStepKind.VerificationDiagnosis,
            claimed.PassNumber,
            null,
            claimed.SourceCheckpointId,
            claimed.SourceFingerprintSha256,
            reportId);
    }

    /// <summary>ADR-0018's coherent result of one execution: bound to this step's checkpoint and fingerprint, completed against the same
    /// fingerprint, and either Passed with exit zero or Failed with a nonzero exit.</summary>
    private static bool IsCoherent(VerificationExecution execution, LocalExecutionSequenceStep step) =>
        execution.GitCheckpointId == step.SourceCheckpointId
        && string.Equals(execution.CheckpointFingerprintSha256, step.SourceFingerprintSha256, StringComparison.Ordinal)
        && string.Equals(execution.CompletionFingerprintSha256, step.SourceFingerprintSha256, StringComparison.Ordinal)
        && execution.DispatchedAtUtc is not null
        && execution.CompletedAtUtc is not null
        && execution.Outcome == VerificationExecutionOutcome.Exited
        && ((execution.Status == VerificationExecutionStatus.Passed && execution.ExitCode == 0)
            || (execution.Status == VerificationExecutionStatus.Failed && execution.ExitCode is not null and not 0));

    /// <summary>The closed enum name as a bounded lowercase detail code (for example <c>local_execution_sequence_dispatch_refused</c>).</summary>
    internal static string? DetailOf(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var character in name)
        {
            if (char.IsUpper(character) && builder.Length > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
