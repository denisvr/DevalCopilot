using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;

/// <summary>
/// Owns the one exact comparison both the dispatch gate and the pre-dispatch refusal command need: whether a
/// verification-diagnosis attempt's input identity — the diagnosed ExecutionReport (its sequence-0
/// <see cref="AttemptInputMessage"/>) plus its exact ordered claimed verification-execution set (its
/// <see cref="AttemptVerificationEvidence"/> rows) — already has a successful diagnosis (findings or escalation) recorded by
/// a different attempt. Exact, ordered comparison on both parts, never an overlap check. A failed diagnosis does not count,
/// so a failed invocation may be explicitly requested again. Mirrors <see cref="CodeReviewInputIdentity"/> for the distinct
/// VerificationDiagnosis contract: an ordinary review of the same identity never matches.
/// </summary>
internal static class VerificationDiagnosisInputIdentity
{
    public static async Task<bool> HasCompetingSuccessfulDiagnosisAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        Guid attemptId,
        Guid executionReportMessageId,
        IReadOnlyList<Guid> orderedVerificationExecutionIds,
        CancellationToken cancellationToken)
    {
        var candidateAttemptIds = await dbContext.Attempts.AsNoTracking()
            .Where(candidate =>
                candidate.Id != attemptId
                && candidate.RunId == runId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.CodeReviewer
                && candidate.AgentResponseContract == AgentResponseContract.VerificationDiagnosis
                && candidate.Status == AttemptStatus.Completed
                && (candidate.AgentOutcome == AgentOutcome.DiagnosisFindingsRecorded
                    || candidate.AgentOutcome == AgentOutcome.DiagnosisEscalated))
            .Join(
                dbContext.AttemptInputMessages.AsNoTracking().Where(
                    inputMessage => inputMessage.Sequence == 0 && inputMessage.CollaborationMessageId == executionReportMessageId),
                candidate => candidate.Id,
                inputMessage => inputMessage.AttemptId,
                (candidate, inputMessage) => candidate.Id)
            .ToListAsync(cancellationToken);
        if (candidateAttemptIds.Count == 0)
        {
            return false;
        }

        var candidateEvidence = await dbContext.AttemptVerificationEvidence.AsNoTracking()
            .Where(evidence => candidateAttemptIds.Contains(evidence.AttemptId))
            .OrderBy(evidence => evidence.AttemptId)
            .ThenBy(evidence => evidence.Sequence)
            .Select(evidence => new { evidence.AttemptId, evidence.VerificationExecutionId })
            .ToListAsync(cancellationToken);

        return candidateEvidence
            .GroupBy(evidence => evidence.AttemptId)
            .Select(group => group.Select(evidence => evidence.VerificationExecutionId).ToList())
            .Any(candidateIds => orderedVerificationExecutionIds.SequenceEqual(candidateIds));
    }

    /// <summary>The ordered (command, execution) pairs this attempt pinned at claim, read afresh and untracked.</summary>
    public static async Task<IReadOnlyList<(Guid CommandId, Guid ExecutionId)>> ReadPinnedPairsAsync(
        IDevalCopilotDbContext dbContext, Guid attemptId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.AttemptVerificationEvidence.AsNoTracking()
            .Where(evidence => evidence.AttemptId == attemptId)
            .OrderBy(evidence => evidence.Sequence)
            .Select(evidence => new { evidence.Sequence, evidence.VerificationCommandId, evidence.VerificationExecutionId })
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.VerificationCommandId, row.VerificationExecutionId)).ToArray();
    }

    /// <summary>The ordered pinned memberships with each stored snapshot digest (null when absent), read afresh and untracked.</summary>
    public static async Task<IReadOnlyList<(Guid CommandId, Guid ExecutionId, string? Snapshot)>> ReadPinnedMembershipsAsync(
        IDevalCopilotDbContext dbContext, Guid attemptId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.AttemptVerificationEvidence.AsNoTracking()
            .Where(evidence => evidence.AttemptId == attemptId)
            .OrderBy(evidence => evidence.Sequence)
            .Select(evidence => new { evidence.VerificationCommandId, evidence.VerificationExecutionId, evidence.SnapshotSha256 })
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.VerificationCommandId, row.VerificationExecutionId, row.SnapshotSha256)).ToArray();
    }

    /// <summary>The diagnosed ExecutionReport (the single sequence-0 input), read afresh and untracked; null otherwise.</summary>
    public static async Task<Guid?> ReadPinnedExecutionReportIdAsync(
        IDevalCopilotDbContext dbContext, Guid attemptId, CancellationToken cancellationToken)
    {
        var inputs = await dbContext.AttemptInputMessages.AsNoTracking()
            .Where(input => input.AttemptId == attemptId)
            .OrderBy(input => input.Sequence)
            .Select(input => new { input.Sequence, input.CollaborationMessageId })
            .ToListAsync(cancellationToken);
        return inputs.Count == 1 && inputs[0].Sequence == 0 ? inputs[0].CollaborationMessageId : null;
    }
}
