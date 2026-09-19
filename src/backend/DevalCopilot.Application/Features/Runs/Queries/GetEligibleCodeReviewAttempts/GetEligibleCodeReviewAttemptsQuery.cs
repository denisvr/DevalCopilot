using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleCodeReviewAttempts;

public sealed record GetEligibleCodeReviewAttemptsQuery : IQuery<IReadOnlyList<EligibleCodeReviewAttempt>>;
