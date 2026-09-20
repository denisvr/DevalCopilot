using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// Owns the exact ordered identity of a ReviewCorrection attempt: the previous
/// ExecutionReport followed by every ReviewFinding in collaboration-timeline order. Candidate
/// discovery is bounded to two database queries and the final comparison is an ordered equality
/// check, never a set or overlap comparison.
/// </summary>
internal static class ReviewCorrectionInputIdentity
{
    public static async Task<List<Guid>> GetOrderedInputMessageIdsAsync(
        IDevalCopilotDbContext dbContext, Guid attemptId, CancellationToken cancellationToken) =>
        await dbContext.AttemptInputMessages
            .Where(inputMessage => inputMessage.AttemptId == attemptId)
            .OrderBy(inputMessage => inputMessage.Sequence)
            .Select(inputMessage => inputMessage.CollaborationMessageId)
            .ToListAsync(cancellationToken);

    public static async Task<bool> HasCompetingSuccessfulCorrectionAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        Guid attemptId,
        Guid startingCheckpointId,
        IReadOnlyList<Guid> orderedInputMessageIds,
        CancellationToken cancellationToken)
    {
        if (orderedInputMessageIds.Count == 0)
        {
            return false;
        }

        var previousExecutionReportId = orderedInputMessageIds[0];
        var candidateAttemptIds = await dbContext.Attempts
            .Where(candidate =>
                candidate.Id != attemptId
                && candidate.RunId == runId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.Implementer
                && candidate.AgentResponseContract == AgentResponseContract.ReviewCorrection
                && candidate.AgentGitCheckpointId == startingCheckpointId
                && candidate.Status == AttemptStatus.Completed
                && candidate.AgentOutcome == AgentOutcome.CorrectionApplied)
            .Join(
                dbContext.AttemptInputMessages.Where(inputMessage =>
                    inputMessage.Sequence == 0 && inputMessage.CollaborationMessageId == previousExecutionReportId),
                candidate => candidate.Id,
                inputMessage => inputMessage.AttemptId,
                (candidate, _) => candidate.Id)
            .ToListAsync(cancellationToken);

        if (candidateAttemptIds.Count == 0)
        {
            return false;
        }

        var candidateInputMessages = await dbContext.AttemptInputMessages
            .Where(inputMessage => candidateAttemptIds.Contains(inputMessage.AttemptId))
            .OrderBy(inputMessage => inputMessage.AttemptId)
            .ThenBy(inputMessage => inputMessage.Sequence)
            .Select(inputMessage => new { inputMessage.AttemptId, inputMessage.CollaborationMessageId })
            .ToListAsync(cancellationToken);

        foreach (var candidateInputMessagesForAttempt in candidateInputMessages.GroupBy(row => row.AttemptId))
        {
            if (orderedInputMessageIds.SequenceEqual(candidateInputMessagesForAttempt.Select(row => row.CollaborationMessageId)))
            {
                return true;
            }
        }

        return false;
    }
}
