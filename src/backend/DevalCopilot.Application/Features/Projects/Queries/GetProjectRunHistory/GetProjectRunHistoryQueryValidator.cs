using FluentValidation;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectRunHistory;

/// <summary>Structural bounds only. A cursor or limit outside them is a validation failure: nothing is clamped or defaulted for it. The
/// messages are fixed text and never repeat the rejected value.</summary>
public sealed class GetProjectRunHistoryQueryValidator : AbstractValidator<GetProjectRunHistoryQuery>
{
    public GetProjectRunHistoryQueryValidator()
    {
        RuleFor(query => query.BeforeExecutionNumber)
            .GreaterThan(0)
            .When(query => query.BeforeExecutionNumber.HasValue)
            .WithErrorCode("validation.invalid_cursor")
            .WithMessage("The cursor must be a positive whole number.");

        RuleFor(query => query.Limit)
            .InclusiveBetween(1, GetProjectRunHistoryQuery.MaximumLimit)
            .When(query => query.Limit.HasValue)
            .WithErrorCode("validation.invalid_limit")
            .WithMessage($"The limit must be a whole number from 1 to {GetProjectRunHistoryQuery.MaximumLimit}.");
    }
}
