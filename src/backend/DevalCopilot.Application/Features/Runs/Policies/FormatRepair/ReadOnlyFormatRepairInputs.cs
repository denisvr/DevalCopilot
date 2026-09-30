using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.FormatRepair;

/// <summary>
/// One attempt's exact recorded input identity as the three read-only repair paths compare it: the
/// ordered <see cref="AttemptInputMessage"/> ids and, for the CodeReviewer, the ordered
/// (verification command, verification execution) pairs. Read only from non-enum scalar columns, so
/// an attempt whose enum columns are unreadable never breaks the read. A shape that is missing,
/// duplicated, gapped, or out of range for the response contract reads as <see langword="null"/>
/// (never partially trusted); equality is exact and ordered — an overlapping, partial, reordered,
/// or foreign set never matches. Used for the repair source at the claim and again at dispatch for
/// the repair attempt itself.
/// </summary>
internal sealed record ReadOnlyFormatRepairInputs(
    IReadOnlyList<Guid> OrderedMessageIds, IReadOnlyList<(Guid CommandId, Guid ExecutionId)> OrderedVerification)
{
    /// <summary>The maximum number of inputs the largest supported shape can carry: the Proposal plus
    /// a bounded Challenge set, or a single report plus a bounded verification set.</summary>
    private const int MaximumInputs = 64;

    public static async Task<ReadOnlyFormatRepairInputs?> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid attemptId, AgentResponseContract responseContract, CancellationToken cancellationToken)
    {
        var messageRows = await dbContext.AttemptInputMessages
            .AsNoTracking()
            .Where(row => row.AttemptId == attemptId)
            .OrderBy(row => row.Sequence)
            .Select(row => new { row.Sequence, row.CollaborationMessageId })
            .Take(MaximumInputs + 1)
            .ToListAsync(cancellationToken);
        if (messageRows.Count == 0
            || messageRows.Count > MaximumInputs
            || messageRows.Select((row, index) => row.Sequence == index).Contains(false)
            || messageRows.Select(row => row.CollaborationMessageId).Distinct().Count() != messageRows.Count)
        {
            return null;
        }

        var messageIds = messageRows.Select(row => row.CollaborationMessageId).ToList();
        switch (responseContract)
        {
            case AgentResponseContract.CriticalReview when messageIds.Count == 1:
                return new ReadOnlyFormatRepairInputs(messageIds, []);

            case AgentResponseContract.ChallengeResolution when messageIds.Count >= 2:
                return new ReadOnlyFormatRepairInputs(messageIds, []);

            case AgentResponseContract.ImplementationReview when messageIds.Count == 1:
                var verificationRows = await dbContext.AttemptVerificationEvidence
                    .AsNoTracking()
                    .Where(row => row.AttemptId == attemptId)
                    .OrderBy(row => row.Sequence)
                    .Select(row => new { row.Sequence, row.VerificationCommandId, row.VerificationExecutionId })
                    .Take(MaximumInputs + 1)
                    .ToListAsync(cancellationToken);
                if (verificationRows.Count == 0
                    || verificationRows.Count > MaximumInputs
                    || verificationRows.Select((row, index) => row.Sequence == index).Contains(false)
                    || verificationRows.Select(row => row.VerificationExecutionId).Distinct().Count() != verificationRows.Count
                    || verificationRows.Select(row => row.VerificationCommandId).Distinct().Count() != verificationRows.Count)
                {
                    return null;
                }

                return new ReadOnlyFormatRepairInputs(
                    messageIds,
                    verificationRows.Select(row => (row.VerificationCommandId, row.VerificationExecutionId)).ToList());

            default:
                return null;
        }
    }

    /// <summary>Exact, ordered equality on both parts.</summary>
    public bool Matches(ReadOnlyFormatRepairInputs? other) =>
        other is not null
        && OrderedMessageIds.SequenceEqual(other.OrderedMessageIds)
        && OrderedVerification.SequenceEqual(other.OrderedVerification);
}
