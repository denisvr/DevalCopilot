namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationMessageEvidence;

/// <summary>
/// The pure gap/duplicate check backing <see cref="AttemptInputMessageEvidenceStatus.Invalid"/>:
/// an attempt's persisted <c>AttemptInputMessage</c> sequence is trustworthy only when it forms the
/// exact gapless run 0, 1, ..., N-1 with no repeated value. Kept as a small, dependency-free unit so
/// every gap and duplicate shape is directly testable without a database.
/// </summary>
internal static class AttemptInputMessageCoherence
{
    /// <summary>
    /// <paramref name="orderedSequences"/> must already be sorted ascending (as an ORDER BY
    /// Sequence query already returns). Returns <see langword="false"/> for any gap (a missing
    /// value), any duplicate (a value repeated instead of advancing), or any negative starting
    /// value — never partially trusting a prefix of an otherwise-broken sequence.
    /// </summary>
    public static bool IsGaplessFromZero(IReadOnlyList<int> orderedSequences)
    {
        for (var index = 0; index < orderedSequences.Count; index++)
        {
            if (orderedSequences[index] != index)
            {
                return false;
            }
        }

        return true;
    }
}
