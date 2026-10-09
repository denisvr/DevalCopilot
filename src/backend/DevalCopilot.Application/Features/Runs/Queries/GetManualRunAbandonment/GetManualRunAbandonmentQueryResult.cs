namespace DevalCopilot.Application.Features.Runs.Queries.GetManualRunAbandonment;

/// <param name="Eligible">Advisory: true only when the run is a created or running manual run and nothing of its project is active or
/// ambiguous at the moment of the read.</param>
/// <param name="RefusalCode">A fixed safe code explaining why the run is not eligible; null when eligible.</param>
/// <param name="Abandonment">The recorded reason and time of a coherently abandoned run; null for every other run, including one whose
/// recorded abandonment facts are not coherent.</param>
public sealed record GetManualRunAbandonmentQueryResult(
    bool Eligible,
    string? RefusalCode,
    ManualRunAbandonmentView? Abandonment,
    long LatestEventSequence);
