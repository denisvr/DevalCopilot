using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;

public sealed record GetRunCockpitQuery(Guid RunId) : IQuery<Result<GetRunCockpitQueryResult>>;
