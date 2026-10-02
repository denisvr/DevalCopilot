using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;

/// <summary>
/// Resolves the structural facts of a completed verification diagnosis that produced findings (ADR-0018), the diagnosis
/// counterpart of <see cref="ReviewCorrectionReviewEligibility"/>: it is the source of a diagnosis-origin correction and of
/// that correction's later validation as a link of an Implementer report chain. Structural resolution reads only the run
/// snapshot; whether the diagnosis still applies to the verification evidence that holds now is the separate, fresh
/// <see cref="VerificationDiagnosisApplicability"/> check. An ordinary implementation review never resolves here and a
/// diagnosis never resolves as a review.
/// </summary>
internal static class VerificationDiagnosisEligibility
{
    internal sealed record CurrentDiagnosis(
        Attempt DiagnosisAttempt,
        CollaborationMessage ExecutionReport,
        IReadOnlyList<CollaborationMessage> OrderedFindings,
        ImplementerExecutionReportEligibility.Result ImplementationLineage);

    /// <summary>The structural resolution of one diagnosis attempt against the workspace's current checkpoint, or null when
    /// it is not a completed, coherent findings diagnosis of a valid, current Implementer report.</summary>
    internal static CurrentDiagnosis? ResolveForAttempt(
        ImplementerExecutionReportEligibility.Snapshot snapshot,
        Attempt diagnosis,
        Guid runId,
        Guid workspaceId,
        Guid checkpointId)
    {
        if (diagnosis.RunId != runId
            || !VerificationDiagnosisPolicy.HasExactTuple(diagnosis)
            || diagnosis.AgentOutcome != AgentOutcome.DiagnosisFindingsRecorded
            || diagnosis.Status != AttemptStatus.Completed
            || diagnosis.AgentGitWorkspaceId != workspaceId
            || diagnosis.AgentGitCheckpointId != checkpointId
            || diagnosis.AgentProvider is not { } provider
            || !Enum.IsDefined(provider))
        {
            return null;
        }

        var inputs = snapshot.InputsFor(diagnosis.Id);
        if (inputs.Count != 1 || inputs[0].Sequence != 0
            || !snapshot.MessagesById.TryGetValue(inputs[0].CollaborationMessageId, out var executionReport)
            || executionReport.Type != CollaborationMessageType.ExecutionReport
            || executionReport.RunId != runId)
        {
            return null;
        }

        var lineage = ImplementerExecutionReportEligibility.Resolve(snapshot, executionReport, runId, workspaceId, checkpointId);
        if (lineage is null)
        {
            return null;
        }

        var owned = snapshot.Messages.Where(message => message.RunId == runId && message.AttemptId == diagnosis.Id).ToArray();
        var findings = owned
            .Where(message => message.Type == CollaborationMessageType.ReviewFinding)
            .OrderBy(message => message.Sequence)
            .ToArray();
        var expectedActor = ParticipantIdentity.ForAgent(AgentRole.CodeReviewer, provider);
        if (owned.Length != findings.Length
            || findings.Length is < VerificationDiagnosisPolicy.MinimumFindings or > VerificationDiagnosisPolicy.MaximumFindings
            || findings.Any(finding => finding.Provenance != CollaborationMessageProvenance.ProviderObserved
                || finding.Actor != expectedActor
                || finding.InReplyToMessageId != executionReport.Id
                || finding.Sequence <= executionReport.Sequence))
        {
            return null;
        }

        return new CurrentDiagnosis(diagnosis, executionReport, findings, lineage);
    }

    /// <summary>
    /// The origin of a ReviewCorrection attempt, read afresh and untracked: the verification-diagnosis attempt that authored
    /// its first finding input, or null when the correction is not diagnosis-origin (an ordinary review-origin correction, or
    /// one without findings). It does not validate the origin; callers that act on it validate with
    /// <see cref="ResolveForAttempt"/> and <see cref="VerificationDiagnosisApplicability"/>.
    /// </summary>
    internal static async Task<Attempt?> FindOriginDiagnosisAsync(
        IDevalCopilotDbContext dbContext, Guid correctionAttemptId, Guid runId, CancellationToken cancellationToken)
    {
        var firstFindingId = await dbContext.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == correctionAttemptId && input.Sequence == 1)
            .Select(input => (Guid?)input.CollaborationMessageId)
            .SingleOrDefaultAsync(cancellationToken);
        if (firstFindingId is null)
        {
            return null;
        }

        var findingAttemptId = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.Id == firstFindingId.Value && message.RunId == runId)
            .Select(message => message.AttemptId)
            .SingleOrDefaultAsync(cancellationToken);
        if (findingAttemptId is null)
        {
            return null;
        }

        var origin = await dbContext.Attempts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == findingAttemptId.Value && candidate.RunId == runId, cancellationToken);
        return origin is not null && origin.AgentResponseContract == AgentResponseContract.VerificationDiagnosis ? origin : null;
    }

    /// <summary>
    /// Whether a (still unfinished) diagnosis-origin correction's source is still exactly applicable now: the origin
    /// diagnosis structurally resolves against the current checkpoint, the correction's ordered finding inputs are exactly its
    /// complete ordered findings, and the verification evidence it was produced against is unchanged.
    /// </summary>
    internal static async Task<bool> IsCorrectionSourceCurrentAsync(
        IDevalCopilotDbContext dbContext, Attempt correction, Attempt originDiagnosis, CancellationToken cancellationToken)
    {
        if (correction.AgentGitWorkspaceId is not { } workspaceId || correction.AgentGitCheckpointId is not { } checkpointId)
        {
            return false;
        }

        try
        {
            var snapshot = await ImplementerExecutionReportEligibility.LoadSnapshotAsync(dbContext, correction.RunId, cancellationToken);
            var resolved = ResolveForAttempt(snapshot, originDiagnosis, correction.RunId, workspaceId, checkpointId);
            if (resolved is null)
            {
                return false;
            }

            var correctionInputs = snapshot.InputsFor(correction.Id).Select(input => input.CollaborationMessageId).ToArray();
            Guid[] expected = [resolved.ExecutionReport.Id, .. resolved.OrderedFindings.Select(finding => finding.Id)];
            return correctionInputs.SequenceEqual(expected)
                && await VerificationDiagnosisApplicability.EvaluateAsync(dbContext, originDiagnosis, cancellationToken)
                    == VerificationDiagnosisApplicability.Verdict.Applicable;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
