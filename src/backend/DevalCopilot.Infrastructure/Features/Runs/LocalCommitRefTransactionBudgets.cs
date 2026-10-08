namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>The independent time limits of one prepared reference transaction. <paramref name="Total"/> covers launch, every
/// protocol phase and the prepared-lock proof; <paramref name="Phase"/> bounds each single protocol exchange; <paramref name="Cleanup"/>
/// is a separate budget for abort, close, termination and reaping that is never derived from the transaction deadline or a
/// caller's token.</summary>
internal sealed record LocalCommitRefTransactionBudgets(TimeSpan Total, TimeSpan Phase, TimeSpan Cleanup)
{
    internal static LocalCommitRefTransactionBudgets Production { get; } =
        new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
}
