namespace DevalCopilot.Application.Features.Runs.Queries.GetAdvanceableLocalExecutionSequences;

/// <summary>One page of the advancing-sequence sweep and the fixed high-water of the finite range it belongs to (ADR-0062).
/// <paramref name="SweepFinished"/> means no record of that range remains after this page.</summary>
public sealed record AdvanceableLocalExecutionSequencesPage(
    IReadOnlyList<AdvanceableLocalExecutionSequence> Items, long HighWaterCoordinationNumber, bool SweepFinished);
