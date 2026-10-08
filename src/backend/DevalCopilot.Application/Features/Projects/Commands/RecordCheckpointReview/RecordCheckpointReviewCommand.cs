using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Commands.RecordCheckpointReview;

/// <param name="VerificationExecutionId">The legacy single-execution evidence form.</param>
/// <param name="VerificationExecutionIds">The optional execution-set form for Human reviews (ADR-0030). Null or omitted selects the
/// legacy form; an empty set may represent only Pending. Supplying both forms is refused, never merged.</param>
public sealed record RecordCheckpointReviewCommand(
    Guid ProjectId,
    Guid GitCheckpointId,
    Guid? VerificationExecutionId,
    ReviewActorKind ActorKind,
    ReviewDecision Decision,
    IReadOnlyList<Guid>? VerificationExecutionIds = null) : IManualTransactionCommand<Result<RecordCheckpointReviewCommandResult>>;
