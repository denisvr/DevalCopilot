using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.AuthorizeReviewCorrection;

public sealed record AuthorizeReviewCorrectionCommand(Guid RunId, Guid EscalationId)
    : IManualTransactionCommand<Result<AuthorizeReviewCorrectionCommandResult>>;
