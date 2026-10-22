using DevalCopilot.Application.Features.Runs.Queries.GetAdvanceableLocalExecutionSequences;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The host's rotating cursor over the advancing-sequence feed (ADR-0062): a sweep fixes a high-water coordination number with its first
/// page, finishes that finite range page by page and only then starts a new sweep with a fresh high-water. Continuous arrivals therefore
/// never keep an old preparing, claimed or stopping record from its next observation (and its deadline), and never lengthen a sweep. It
/// carries no authority; every observation re-decides from locked durable state.
/// </summary>
public sealed class LocalExecutionAdvancingSweep
{
    private long after;
    private long? highWater;

    /// <summary>The query of the next page of the current sweep (or the first page of a new one).</summary>
    public GetAdvanceableLocalExecutionSequencesQuery NextQuery() => new(after, highWater);

    /// <summary>Records a page: a finished (or empty) range starts a new sweep next time, otherwise the cursor and the fixed high-water carry on.</summary>
    public void Accept(AdvanceableLocalExecutionSequencesPage page)
    {
        if (page.SweepFinished || page.Items.Count == 0)
        {
            after = 0;
            highWater = null;
            return;
        }

        after = page.Items[^1].CoordinationNumber;
        highWater = page.HighWaterCoordinationNumber;
    }
}
