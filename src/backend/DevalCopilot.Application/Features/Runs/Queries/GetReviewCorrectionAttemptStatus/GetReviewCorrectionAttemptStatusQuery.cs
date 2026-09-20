using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetReviewCorrectionAttemptStatus;

public sealed record GetReviewCorrectionAttemptStatusQuery(Guid RunId)
    : IQuery<Result<ReviewCorrectionAttemptStatusQueryResult>>;
