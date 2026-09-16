using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Projects.Queries.GetEligibleVerificationExecutions;

public sealed record GetEligibleVerificationExecutionsQuery : IQuery<IReadOnlyList<EligibleVerificationExecution>>;
