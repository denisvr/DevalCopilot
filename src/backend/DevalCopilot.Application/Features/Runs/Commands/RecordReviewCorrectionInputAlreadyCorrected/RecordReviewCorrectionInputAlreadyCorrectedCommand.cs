using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordReviewCorrectionInputAlreadyCorrected;

public sealed record RecordReviewCorrectionInputAlreadyCorrectedCommand(Guid RunId, Guid AttemptId)
    : ICommand<Result>;
