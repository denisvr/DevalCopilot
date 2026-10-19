using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAdvanceablePlanningSequences;

/// <summary>
/// The bounded set of planning sequences whose automatic authority is still live (Armed, preparing, awaiting a result or stopping), oldest
/// first (ADR-0055). It is only the coordinator's observation feed: it authorizes nothing and decides nothing, so a stale answer is
/// harmless because every observation re-decides from locked durable state.
/// </summary>
public sealed record GetAdvanceablePlanningSequencesQuery : IQuery<IReadOnlyList<AdvanceablePlanningSequence>>;
