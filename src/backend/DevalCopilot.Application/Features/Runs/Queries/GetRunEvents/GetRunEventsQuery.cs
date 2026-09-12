using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunEvents;

public sealed record GetRunEventsQuery(Guid RunId, long AfterSequence) : IQuery<Result<IReadOnlyList<RunEventQueryResult>>>;
