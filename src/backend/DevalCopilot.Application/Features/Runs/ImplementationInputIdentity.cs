using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// Owns the one comparison both <c>MarkAgentAttemptDispatchedCommandHandler</c>'s Implementer gate
/// and <c>CreateImplementationAttemptCommandHandler</c> need: whether a different attempt has
/// already successfully implemented the exact same resolved plan (identified by its sequence-0
/// Proposal message id — every eligible resolved-plan form is bound to exactly one Proposal
/// message, never a set) against the exact same starting checkpoint. Unlike
/// <c>ChallengeResolutionInputIdentity</c>, this never needs a full ordered-sequence comparison:
/// an implementation attempt's "plan identity" is single-message by construction, so the
/// sequence-0 Proposal id plus the starting checkpoint id is already the complete, exact identity.
/// </summary>
internal static class ImplementationInputIdentity
{
    public static async Task<Guid> GetPlanProposalMessageIdAsync(
        IDevalCopilotDbContext dbContext, Guid attemptId, CancellationToken cancellationToken) =>
        await dbContext.AttemptInputMessages
            .Where(inputMessage => inputMessage.AttemptId == attemptId && inputMessage.Sequence == 0)
            .Select(inputMessage => inputMessage.CollaborationMessageId)
            .SingleAsync(cancellationToken);

    public static async Task<bool> HasCompetingSuccessfulImplementationAsync(
        IDevalCopilotDbContext dbContext,
        Guid runId,
        Guid attemptId,
        Guid planProposalMessageId,
        Guid startingCheckpointId,
        CancellationToken cancellationToken) =>
        await dbContext.Attempts
            .Where(candidate =>
                candidate.Id != attemptId
                && candidate.RunId == runId
                && candidate.Kind == AttemptKind.Agent
                && candidate.AgentRole == AgentRole.Implementer
                && candidate.AgentResponseContract == AgentResponseContract.ImplementationReport
                && candidate.Status == AttemptStatus.Completed
                && candidate.AgentOutcome == AgentOutcome.Implemented
                && candidate.AgentGitCheckpointId == startingCheckpointId)
            .Join(
                dbContext.AttemptInputMessages.Where(
                    inputMessage => inputMessage.Sequence == 0 && inputMessage.CollaborationMessageId == planProposalMessageId),
                candidate => candidate.Id,
                inputMessage => inputMessage.AttemptId,
                (candidate, inputMessage) => candidate.Id)
            .AnyAsync(cancellationToken);
}
