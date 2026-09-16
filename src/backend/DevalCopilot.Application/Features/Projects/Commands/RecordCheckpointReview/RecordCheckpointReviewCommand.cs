using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordCheckpointReview;

public sealed record RecordCheckpointReviewCommand(
    Guid ProjectId,
    Guid GitCheckpointId,
    Guid? VerificationExecutionId,
    ReviewActorKind ActorKind,
    ReviewDecision Decision) : IManualTransactionCommand<Result<RecordCheckpointReviewCommandResult>>;
