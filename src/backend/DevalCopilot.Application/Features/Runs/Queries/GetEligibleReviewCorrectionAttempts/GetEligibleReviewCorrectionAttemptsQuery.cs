using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleReviewCorrectionAttempts;

public sealed record GetEligibleReviewCorrectionAttemptsQuery : IQuery<IReadOnlyList<EligibleReviewCorrectionAttempt>>;
