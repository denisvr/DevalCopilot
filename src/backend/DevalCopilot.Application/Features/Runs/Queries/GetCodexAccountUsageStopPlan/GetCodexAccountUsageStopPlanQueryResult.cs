namespace DevalCopilot.Application.Features.Runs.Queries.GetCodexAccountUsageStopPlan;

/// <summary>What a claimed Codex attempt's own immutable snapshot requires of the dispatch guard, and the valid threshold when it
/// requires an observation.</summary>
public sealed record GetCodexAccountUsageStopPlanQueryResult(CodexAccountUsageStopPlanState State, int? ThresholdPercent);
