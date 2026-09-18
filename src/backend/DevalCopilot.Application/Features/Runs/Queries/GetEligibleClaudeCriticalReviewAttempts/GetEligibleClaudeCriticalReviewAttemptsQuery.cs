using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleClaudeCriticalReviewAttempts;

public sealed record GetEligibleClaudeCriticalReviewAttemptsQuery : IQuery<IReadOnlyList<EligibleClaudeCriticalReviewAttempt>>;
