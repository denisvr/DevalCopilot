using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleAgentAttempts;

public sealed record GetEligibleAgentAttemptsQuery : IQuery<IReadOnlyList<EligibleAgentAttempt>>;
