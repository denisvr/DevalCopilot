namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

/// <param name="Attribution">The provider bucket this entry summarizes.</param>
/// <param name="Summary">Exactly the same run-total-eligibility rules as the run-wide
/// <see cref="RunCockpitTokenUsageSummary"/>, scoped to only the dispatched Agent attempts attributed
/// to this bucket. Only this bucket's own <see cref="RunTokenUsageCompleteness.Complete"/> state may
/// ever be shown as that provider's total.</param>
public sealed record RunCockpitProviderTokenUsageEntry(
    RunCockpitProviderTokenUsageAttribution Attribution,
    RunCockpitTokenUsageSummary Summary);
