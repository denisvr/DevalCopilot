using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

namespace DevalCopilot.Api.Features.Runs.GetRunCockpit;

/// <summary>
/// The API projection of one provider bucket's token-usage summary from the cockpit's provider-
/// separated token-usage projection. <c>Attribution</c> crosses the wire as a plain string (this
/// repo's established convention — zero generated TypeScript <c>enum</c>): <c>"Codex"</c>,
/// <c>"ClaudeCode"</c>, or <c>"Unattributed"</c>. Always exactly three entries, one per attribution,
/// even when a bucket has no dispatched attempts. Each entry's own <c>Summary</c> follows exactly the
/// same run-total-eligibility rules as the existing run-wide <see cref="RunTokenUsageSummaryResponse"/>,
/// scoped to only that bucket's dispatched Agent attempts, and never replaces or changes the existing
/// run-wide <c>tokenUsageSummary</c> field.
/// </summary>
public sealed record RunCockpitProviderTokenUsageEntryResponse(string Attribution, RunTokenUsageSummaryResponse Summary)
{
    public static RunCockpitProviderTokenUsageEntryResponse FromDomain(RunCockpitProviderTokenUsageEntry entry) =>
        new(entry.Attribution.ToString(), RunTokenUsageSummaryResponse.FromSummary(entry.Summary));
}
