using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordCheckpointReview;

public sealed record RecordCheckpointReviewCommandResult(Guid ReviewId, ReviewDecision Decision);
