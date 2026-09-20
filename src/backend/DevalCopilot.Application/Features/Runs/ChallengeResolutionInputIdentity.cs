using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// Owns the one comparison both <c>MarkAgentAttemptDispatchedCommandHandler</c>'s Resolver gate
/// and <c>RecordChallengeResolutionInputAlreadyResolvedCommandHandler</c> need: whether a
/// challenge-resolution attempt's exact, ordered input identity — the original Proposal at
/// sequence 0, then its complete Challenge set in timeline order — already has a successful
/// resolution recorded against it by a different attempt. Deliberately exact-sequence
/// comparison, never a set/overlap check: a partial, foreign, reordered, or merely overlapping
/// input set must never match, since only an attempt claimed against the identical Challenged
/// review (by <c>CreateChallengeResolutionAttemptCommandHandler</c>) can ever produce the same
/// ordered sequence in practice.
/// </summary>
internal static class ChallengeResolutionInputIdentity
{
    public static async Task<List<Guid>> GetOrderedInputMessageIdsAsync(
        IDevalCopilotDbContext dbContext, Guid attemptId, CancellationToken cancellationToken) =>
        await dbContext.AttemptInputMessages
            .Where(inputMessage => inputMessage.AttemptId == attemptId)
            .OrderBy(inputMessage => inputMessage.Sequence)
            .Select(inputMessage => inputMessage.CollaborationMessageId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Never trusts a caller's own claim: always re-reads every candidate's own ordered input
    /// set fresh from the database and compares it exactly, element by element and in order,
    /// against <paramref name="orderedInputMessageIds"/>. Bounded to a fixed number of database
    /// round trips regardless of how many candidate attempts exist: candidate identification and
    /// candidate input-set retrieval are each exactly one query, never one query per candidate.
    /// The run/role/contract/outcome filter and the sequence-0 Proposal-id join below are only a
    /// narrowing prefilter — cheap to express in SQL, and correct because only an attempt sharing
    /// this run and this exact original Proposal could ever share the full ordered set — but
    /// they never replace the final in-memory <c>SequenceEqual</c>, which remains the sole source
    /// of truth for "the same exact input identity."
    /// </summary>
    public static async Task<bool> HasCompetingExactResolutionAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        Guid attemptId,
        IReadOnlyList<Guid> orderedInputMessageIds,
        CancellationToken cancellationToken)
    {
        if (orderedInputMessageIds.Count == 0)
        {
            // No original Proposal to prefilter on at all — a genuine Resolver attempt always
            // has one, so this is unreachable in practice, but never something to compare
            // against as if it could still match.
            return false;
        }

        var originalProposalMessageId = orderedInputMessageIds[0];

        var candidateAttemptIds = await dbContext.Attempts
            .Where(candidate =>
                candidate.Id != attemptId
                && candidate.RunId == runId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.Resolver
                && candidate.AgentResponseContract == AgentResponseContract.ChallengeResolution
                && candidate.Status == AttemptStatus.Completed
                && candidate.AgentOutcome == AgentOutcome.Resolved)
            .Join(
                dbContext.AttemptInputMessages.Where(
                    inputMessage => inputMessage.Sequence == 0 && inputMessage.CollaborationMessageId == originalProposalMessageId),
                candidate => candidate.Id,
                inputMessage => inputMessage.AttemptId,
                (candidate, inputMessage) => candidate.Id)
            .ToListAsync(cancellationToken);

        if (candidateAttemptIds.Count == 0)
        {
            return false;
        }

        // One query for every candidate's complete ordered input set, never one query per
        // candidate — the in-memory grouping below preserves this ordering because the rows are
        // already sorted by (AttemptId, Sequence) before grouping.
        var candidateInputMessages = await dbContext.AttemptInputMessages
            .Where(inputMessage => candidateAttemptIds.Contains(inputMessage.AttemptId))
            .OrderBy(inputMessage => inputMessage.AttemptId)
            .ThenBy(inputMessage => inputMessage.Sequence)
            .Select(inputMessage => new { inputMessage.AttemptId, inputMessage.CollaborationMessageId })
            .ToListAsync(cancellationToken);

        var orderedInputMessageIdsByCandidate = candidateInputMessages
            .GroupBy(inputMessage => inputMessage.AttemptId)
            .Select(group => group.Select(inputMessage => inputMessage.CollaborationMessageId).ToList());

        foreach (var candidateOrderedInputMessageIds in orderedInputMessageIdsByCandidate)
        {
            if (orderedInputMessageIds.SequenceEqual(candidateOrderedInputMessageIds))
            {
                return true;
            }
        }

        return false;
    }
}
