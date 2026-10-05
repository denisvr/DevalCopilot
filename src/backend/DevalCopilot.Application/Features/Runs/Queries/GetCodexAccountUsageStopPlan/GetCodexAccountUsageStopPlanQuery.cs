using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCodexAccountUsageStopPlan;

/// <summary>The host-internal read a Codex supervisor makes just before dispatch: what the claimed attempt's own stored threshold
/// snapshot requires of the dispatch guard (ADR-0025). It is read afresh and untracked from the attempt row, never from a feed
/// projection. No HTTP contract reaches it.</summary>
public sealed record GetCodexAccountUsageStopPlanQuery(Guid RunId, Guid AttemptId) : IQuery<GetCodexAccountUsageStopPlanQueryResult>;
