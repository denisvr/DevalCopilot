using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// Owns the one comparison both <c>MarkAgentAttemptDispatchedCommandHandler</c>'s CodeReviewer gate
/// and <c>RecordCodeReviewInputAlreadyCodeReviewedCommandHandler</c> need: whether a code-review
/// attempt's exact, ordered input identity — the reviewed Implementation ExecutionReport (recorded
/// as its sequence-0 <see cref="AttemptInputMessage"/>) plus its exact ordered claimed
/// verification-execution set (recorded as its <see cref="AttemptVerificationEvidence"/> rows) —
/// already has a successful review recorded against it by a different attempt. Deliberately exact
/// comparison on both parts, never a set/overlap check on either: a partial, foreign, reordered, or
/// merely overlapping verification set, or a different ExecutionReport, must never match. Mirrors
/// <c>ChallengeResolutionInputIdentity</c> exactly, one level further down the collaboration
/// protocol.
/// </summary>
internal static class CodeReviewInputIdentity
{
    public static async Task<Guid> GetExecutionReportMessageIdAsync(
        IDevalCopilotDbContext dbContext, Guid attemptId, CancellationToken cancellationToken) =>
        await dbContext.AttemptInputMessages
            .Where(inputMessage => inputMessage.AttemptId == attemptId && inputMessage.Sequence == 0)
            .Select(inputMessage => inputMessage.CollaborationMessageId)
            .SingleAsync(cancellationToken);

    public static async Task<List<Guid>> GetOrderedVerificationExecutionIdsAsync(
        IDevalCopilotDbContext dbContext, Guid attemptId, CancellationToken cancellationToken) =>
        await dbContext.AttemptVerificationEvidence
            .Where(evidence => evidence.AttemptId == attemptId)
            .OrderBy(evidence => evidence.Sequence)
            .Select(evidence => evidence.VerificationExecutionId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Never trusts a caller's own claim: always re-reads every candidate's own ordered
    /// verification set fresh from the database and compares it exactly, element by element and in
    /// order, against <paramref name="orderedVerificationExecutionIds"/>. Bounded to a fixed number
    /// of database round trips regardless of how many candidate attempts exist. The
    /// run/role/contract/outcome filter and the sequence-0 ExecutionReport-id join below are only a
    /// narrowing prefilter — correct because only an attempt sharing this run and this exact
    /// ExecutionReport could ever share the full ordered verification set — but they never replace
    /// the final in-memory <c>SequenceEqual</c>, which remains the sole source of truth for "the
    /// same exact input identity."
    /// </summary>
    public static async Task<bool> HasCompetingSuccessfulReviewAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        Guid attemptId,
        Guid executionReportMessageId,
        IReadOnlyList<Guid> orderedVerificationExecutionIds,
        CancellationToken cancellationToken)
    {
        var candidateAttemptIds = await dbContext.Attempts
            .Where(candidate =>
                candidate.Id != attemptId
                && candidate.RunId == runId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentProvider == AgentProvider.Codex
                && candidate.AgentRole == AgentRole.CodeReviewer
                && candidate.AgentResponseContract == AgentResponseContract.ImplementationReview
                && candidate.Status == AttemptStatus.Completed
                && (candidate.AgentOutcome == AgentOutcome.ReviewApproved || candidate.AgentOutcome == AgentOutcome.ReviewChangesRequested))
            .Join(
                dbContext.AttemptInputMessages.Where(
                    inputMessage => inputMessage.Sequence == 0 && inputMessage.CollaborationMessageId == executionReportMessageId),
                candidate => candidate.Id,
                inputMessage => inputMessage.AttemptId,
                (candidate, inputMessage) => candidate.Id)
            .ToListAsync(cancellationToken);

        if (candidateAttemptIds.Count == 0)
        {
            return false;
        }

        var candidateVerificationEvidence = await dbContext.AttemptVerificationEvidence
            .Where(evidence => candidateAttemptIds.Contains(evidence.AttemptId))
            .OrderBy(evidence => evidence.AttemptId)
            .ThenBy(evidence => evidence.Sequence)
            .Select(evidence => new { evidence.AttemptId, evidence.VerificationExecutionId })
            .ToListAsync(cancellationToken);

        var orderedVerificationExecutionIdsByCandidate = candidateVerificationEvidence
            .GroupBy(evidence => evidence.AttemptId)
            .Select(group => group.Select(evidence => evidence.VerificationExecutionId).ToList());

        foreach (var candidateOrderedVerificationExecutionIds in orderedVerificationExecutionIdsByCandidate)
        {
            if (orderedVerificationExecutionIds.SequenceEqual(candidateOrderedVerificationExecutionIds))
            {
                return true;
            }
        }

        return false;
    }
}
