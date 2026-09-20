using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;

/// <summary>Claims one durable Implementer correction attempt for the exact completed
/// implementation review identified by <paramref name="ImplementationReviewAttemptId"/>.</summary>
public sealed record CreateReviewCorrectionAttemptCommand(Guid RunId, Guid ImplementationReviewAttemptId)
    : IManualTransactionCommand<Result<CreateReviewCorrectionAttemptCommandResult>>;
