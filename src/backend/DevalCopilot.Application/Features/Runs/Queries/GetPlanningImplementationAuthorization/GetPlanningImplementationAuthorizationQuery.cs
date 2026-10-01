using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetPlanningImplementationAuthorization;

public sealed record GetPlanningImplementationAuthorizationQuery(Guid RunId, Guid EscalationMessageId)
    : IQuery<Result<PlanningImplementationAuthorizationQueryResult>>;
