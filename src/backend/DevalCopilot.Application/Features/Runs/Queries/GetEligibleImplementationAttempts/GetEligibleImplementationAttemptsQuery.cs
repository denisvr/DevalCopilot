using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleImplementationAttempts;

public sealed record GetEligibleImplementationAttemptsQuery : IQuery<IReadOnlyList<EligibleImplementationAttempt>>;
