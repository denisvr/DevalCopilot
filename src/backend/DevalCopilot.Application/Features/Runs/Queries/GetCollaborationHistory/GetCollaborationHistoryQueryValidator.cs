using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistory;

/// <summary>Structural bounds only. A cursor or limit outside them is a validation failure: nothing is clamped or defaulted for it.
/// The messages are fixed text and never repeat the rejected value.</summary>
public sealed class GetCollaborationHistoryQueryValidator : AbstractValidator<GetCollaborationHistoryQuery>
{
    public GetCollaborationHistoryQueryValidator()
    {
        RuleFor(query => query.BeforeSequence)
            .Must(cursor => CollaborationHistoryCursor.TryParse(cursor, out _))
            .When(query => query.BeforeSequence is not null)
            .WithErrorCode("validation.invalid_cursor")
            .WithMessage("The cursor must be a positive whole number written without a sign, padding or leading zero.");

        RuleFor(query => query.Limit)
            .InclusiveBetween(1, GetCollaborationHistoryQuery.MaximumLimit)
            .When(query => query.Limit.HasValue)
            .WithErrorCode("validation.invalid_limit")
            .WithMessage($"The limit must be a whole number from 1 to {GetCollaborationHistoryQuery.MaximumLimit}.");
    }
}
