using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetOpenAgentSupervisions;

/// <summary>
/// One bounded window of the open Agent supervision observations, in a fixed total order starting after <paramref name="After"/> and
/// wrapping to the start (ADR-0056). Handing the returned cursor back each round visits every open observation eventually, so none
/// starves behind another. It is only the independent sweep's feed: it authorizes nothing and a stale answer is harmless because each
/// observation re-decides from locked durable state.
/// </summary>
public sealed record GetOpenAgentSupervisionsQuery(Guid? After) : IQuery<OpenAgentSupervisionWindow>;
