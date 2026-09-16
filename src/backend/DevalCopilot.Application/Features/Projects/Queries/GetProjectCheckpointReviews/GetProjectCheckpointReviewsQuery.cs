using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectCheckpointReviews;

public sealed record GetProjectCheckpointReviewsQuery(Guid ProjectId)
    : IQuery<Result<IReadOnlyList<CheckpointReviewQueryResult>>>;
