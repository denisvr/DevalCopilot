using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The one proof of the actual final evidence a local execution sequence advertises (ADR-0060): the recorded ReviewApproval of the exact
/// linked review, or the Escalation (host exhaustion or the provider diagnosis) that ended the cycle. The coordinator's classification
/// and the later status disclosure share it, so a final reference is never taken from a step row or an attempt outcome alone: the
/// message must exist, belong to the run, carry the expected provenance, actor and reply, and the operation that produced it must still
/// satisfy its identity, source, input and pass relationships. Missing or incoherent evidence fails closed; nothing is replayed,
/// replaced or repaired, and the immutable recorded outcomes stay as they are.
/// </summary>
internal static class LocalExecutionFinalEvidence
{
    /// <summary>The fixed summary of the host exhaustion Escalation (ADR-0060).</summary>
    public const string HostExhaustionSummary = "Local execution cycle requires a human decision.";

    /// <summary>Whether the linked CodeReview attempt really approved the exact linked report for the exact recorded pass.</summary>
    public static async Task<bool> ApprovalHoldsAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        LocalExecutionSequenceStep reviewStep,
        Attempt attempt,
        CancellationToken cancellationToken)
    {
        if (reviewStep.Kind != LocalExecutionStepKind.CodeReview
            || reviewStep.SequenceId != sequence.Id
            || reviewStep.OperationId != attempt.Id
            || reviewStep.ReportMessageId is not { } reportId
            || attempt.Kind != AttemptKind.Agent
            || attempt.RunId != sequence.RunId
            || attempt.Status != AttemptStatus.Completed
            || attempt.AgentResponseContract != AgentResponseContract.ImplementationReview
            || attempt.AgentOutcome != AgentOutcome.ReviewApproved
            || attempt.AgentRole is not { } role
            || attempt.AgentProvider is not { } provider
            || attempt.AgentGitWorkspaceId != sequence.WorkspaceId
            || attempt.AgentGitCheckpointId != reviewStep.SourceCheckpointId
            || !string.Equals(attempt.AgentCheckpointFingerprintSha256, reviewStep.SourceFingerprintSha256, StringComparison.Ordinal)
            || !LocalExecutionExitProof.Holds(attempt)
            || attempt.GetAgentProcessExecutionEvidence() is not { IsCleanExit: true })
        {
            return false;
        }

        if (!await dbContext.CollaborationMessages.AsNoTracking().AnyAsync(
                message => message.Id == reportId && message.RunId == sequence.RunId && message.Type == CollaborationMessageType.ExecutionReport,
                cancellationToken))
        {
            return false;
        }

        if (!await InputsAndPassHoldAsync(dbContext, sequence, attempt, reportId, reviewStep.PassNumber, cancellationToken))
        {
            return false;
        }

        var approvals = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.AttemptId == attempt.Id && message.Type == CollaborationMessageType.ReviewApproval)
            .ToListAsync(cancellationToken);
        if (approvals.Count != 1
            || approvals[0].RunId != sequence.RunId
            || approvals[0].Provenance != CollaborationMessageProvenance.ProviderObserved
            || approvals[0].Actor != ParticipantIdentity.ForAgent(role, provider)
            || approvals[0].InReplyToMessageId != reportId)
        {
            return false;
        }

        return true;
    }

    /// <summary>Whether the attempt's one recorded input is exactly the linked report and its verification evidence is exactly the
    /// recorded pass: that pass's executions, one per pinned recipe, in recipe order. A missing, substituted, extra or reordered member
    /// of either set fails.</summary>
    private static async Task<bool> InputsAndPassHoldAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        Attempt attempt,
        Guid reportId,
        int passNumber,
        CancellationToken cancellationToken)
    {
        var inputs = await dbContext.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == attempt.Id)
            .OrderBy(input => input.Sequence)
            .Select(input => input.CollaborationMessageId)
            .ToListAsync(cancellationToken);
        if (inputs.Count != 1 || inputs[0] != reportId)
        {
            return false;
        }

        var pass = await LocalExecutionStepAuthority.PassExecutionsAsync(dbContext, sequence.Id, passNumber, cancellationToken);
        var evidence = await dbContext.AttemptVerificationEvidence.AsNoTracking()
            .Where(row => row.AttemptId == attempt.Id)
            .OrderBy(row => row.Sequence)
            .Select(row => row.VerificationExecutionId)
            .ToListAsync(cancellationToken);
        return pass.Count == sequence.RecipeCount
            && pass.Select((member, index) => member.RecipeOrdinal == index).All(same => same)
            && evidence.SequenceEqual(pass.Select(member => member.ExecutionId));
    }

    /// <summary>Whether the provider diagnosis Escalation really answers the exact linked report of its linked diagnosis attempt.</summary>
    public static async Task<bool> ProviderEscalationHoldsAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        LocalExecutionSequenceStep diagnosisStep,
        Attempt attempt,
        Guid escalationMessageId,
        CancellationToken cancellationToken)
    {
        if (diagnosisStep.Kind != LocalExecutionStepKind.VerificationDiagnosis
            || diagnosisStep.SequenceId != sequence.Id
            || diagnosisStep.OperationId != attempt.Id
            || diagnosisStep.ReportMessageId is not { } reportId
            || attempt.Kind != AttemptKind.Agent
            || attempt.RunId != sequence.RunId
            || attempt.Status != AttemptStatus.Completed
            || attempt.AgentResponseContract != AgentResponseContract.VerificationDiagnosis
            || attempt.AgentOutcome != AgentOutcome.DiagnosisEscalated
            || attempt.AgentRole is not { } role
            || attempt.AgentProvider is not { } provider
            || attempt.AgentGitWorkspaceId != sequence.WorkspaceId
            || attempt.AgentGitCheckpointId != diagnosisStep.SourceCheckpointId
            || !string.Equals(attempt.AgentCheckpointFingerprintSha256, diagnosisStep.SourceFingerprintSha256, StringComparison.Ordinal)
            || !LocalExecutionExitProof.Holds(attempt)
            || attempt.GetAgentProcessExecutionEvidence() is not { IsCleanExit: true })
        {
            return false;
        }

        if (!await InputsAndPassHoldAsync(dbContext, sequence, attempt, reportId, diagnosisStep.PassNumber, cancellationToken))
        {
            return false;
        }

        var escalations = await dbContext.CollaborationMessages.AsNoTracking()
            .Where(message => message.AttemptId == attempt.Id && message.Type == CollaborationMessageType.Escalation)
            .ToListAsync(cancellationToken);
        return escalations.Count == 1
            && escalations[0].Id == escalationMessageId
            && escalations[0].RunId == sequence.RunId
            && escalations[0].Provenance == CollaborationMessageProvenance.ProviderObserved
            && escalations[0].Actor == ParticipantIdentity.ForAgent(role, provider)
            && escalations[0].InReplyToMessageId == reportId
            && await dbContext.CollaborationMessages.AsNoTracking().AnyAsync(
                message => message.Id == reportId && message.RunId == sequence.RunId && message.Type == CollaborationMessageType.ExecutionReport,
                cancellationToken);
    }

    /// <summary>Whether the host exhaustion Escalation is the recorded, fixed, host-constructed message answering the exact current report.</summary>
    public static async Task<bool> HostEscalationHoldsAsync(
        IDevalCopilotDbContext dbContext,
        LocalExecutionSequence sequence,
        IReadOnlyList<LocalExecutionSequenceStep> steps,
        Guid escalationMessageId,
        Guid reportMessageId,
        CancellationToken cancellationToken)
    {
        if (!steps.Any(step => step.ReportMessageId == reportMessageId))
        {
            return false;
        }

        var message = await dbContext.CollaborationMessages.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == escalationMessageId, cancellationToken);
        return message is not null
            && message.RunId == sequence.RunId
            && message.AttemptId is null
            && message.Type == CollaborationMessageType.Escalation
            && message.Provenance == CollaborationMessageProvenance.HostConstructed
            && message.Actor == ParticipantIdentity.ForOrchestrator()
            && message.InReplyToMessageId == reportMessageId
            && string.Equals(message.Summary, HostExhaustionSummary, StringComparison.Ordinal)
            && await dbContext.CollaborationMessages.AsNoTracking().AnyAsync(
                report => report.Id == reportMessageId && report.RunId == sequence.RunId && report.Type == CollaborationMessageType.ExecutionReport,
                cancellationToken);
    }
}
