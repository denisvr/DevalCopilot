using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// Derives, from a linked Agent attempt's own durable rows only, the exact ExecutionReport and result checkpoint that may become the
/// next source of a local execution sequence (ADR-0060). The complete ImplementedPlan versus OriginalProposal identity and the
/// correction chain are validated by <see cref="ImplementerExecutionReportEligibility"/>; on top of that the attempt must have exited
/// cleanly with host-recorded evidence, consumed exactly the step's expected ordered inputs, started from the step's source and produced
/// the workspace's latest checkpoint. A new unrelated checkpoint, plan, verification execution or source edit never becomes the
/// sequence's source, and nothing is selected by "latest message".
/// </summary>
internal static class LocalExecutionResultSource
{
    public static async Task<LocalExecutionAgentResult?> TryDeriveAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        LocalExecutionSequenceStep step,
        Attempt attempt,
        CancellationToken cancellationToken)
    {
        var isCorrection = LocalExecutionSequencePolicy.IsCorrectionKind(step.Kind);
        var expectedOutcome = isCorrection ? AgentOutcome.CorrectionApplied : AgentOutcome.Implemented;
        if ((step.Kind != LocalExecutionStepKind.Implementation && !isCorrection)
            || attempt.Kind != AttemptKind.Agent
            || attempt.RunId != sequence.RunId
            || attempt.Status != AttemptStatus.Completed
            || attempt.AgentResponseContract != LocalExecutionSequencePolicy.ContractOf(step.Kind)
            || attempt.AgentOutcome != expectedOutcome
            || attempt.AgentGitWorkspaceId != sequence.WorkspaceId
            || attempt.AgentGitCheckpointId != step.SourceCheckpointId
            || attempt.AgentResultGitCheckpointId is not { } resultCheckpointId
            || attempt.GetAgentProcessExecutionEvidence() is not { IsCleanExit: true })
        {
            return null;
        }

        var snapshot = await ImplementerExecutionReportEligibility.LoadSnapshotAsync(dbContext, sequence.RunId, cancellationToken);
        var reports = snapshot.Messages
            .Where(message => message.AttemptId == attempt.Id
                && message.Type == CollaborationMessageType.ExecutionReport
                && message.Provenance == CollaborationMessageProvenance.ProviderObserved)
            .ToList();
        if (reports.Count != 1
            || snapshot.CurrentCheckpointFor(sequence.WorkspaceId) != resultCheckpointId
            || ImplementerExecutionReportEligibility.Resolve(
                snapshot, reports[0], sequence.RunId, sequence.WorkspaceId, resultCheckpointId) is not { } resolved
            || resolved.OwnerAttempt.Id != attempt.Id)
        {
            return null;
        }

        var inputs = snapshot.InputsFor(attempt.Id).Select(input => input.CollaborationMessageId).ToList();
        if (!isCorrection)
        {
            var grantId = sequence.PlanAuthority == LocalExecutionPlanAuthority.HumanAuthorizedRevision
                ? snapshot.PlanningAuthorizations
                    .Where(grant => grant.ConsumedByAttemptId == attempt.Id)
                    .Select(grant => (Guid?)grant.Id)
                    .SingleOrDefault()
                : null;
            if (resolved.PreviousExecutionReport is not null
                || resolved.ImplementedPlan.Id != sequence.PlanProposalMessageId
                || !string.Equals(
                    LocalExecutionCandidate.HashPlanInputs(inputs, grantId), sequence.PlanInputsSha256, StringComparison.Ordinal))
            {
                return null;
            }
        }
        else
        {
            var findingIds = resolved.OrderedFindings.Select(finding => finding.Id).ToList();
            if (resolved.PreviousExecutionReport?.Id != step.ReportMessageId
                || resolved.OrderedFindings.Count == 0
                || resolved.OrderedFindings.Any(finding => finding.AttemptId != step.SubjectAttemptId)
                || !inputs.SequenceEqual(new[] { step.ReportMessageId!.Value }.Concat(findingIds)))
            {
                return null;
            }
        }

        var checkpoint = await dbContext.GitCheckpoints.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == resultCheckpointId, cancellationToken);
        return checkpoint is null || checkpoint.WorkspaceId != sequence.WorkspaceId
            ? null
            : new LocalExecutionAgentResult(reports[0].Id, checkpoint.Id, checkpoint.FingerprintSha256);
    }
}
