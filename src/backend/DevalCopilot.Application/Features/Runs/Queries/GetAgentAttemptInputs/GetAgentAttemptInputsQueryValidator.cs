using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptInputs;

/// <summary>Structural bounds only. A limit outside them is a validation failure: nothing is clamped or defaulted for it. The message is
/// fixed text and never repeats the rejected value.</summary>
public sealed class GetAgentAttemptInputsQueryValidator : AbstractValidator<GetAgentAttemptInputsQuery>
{
    public GetAgentAttemptInputsQueryValidator()
    {
        RuleFor(query => query.Limit)
            .InclusiveBetween(1, GetAgentAttemptInputsQuery.MaximumLimit)
            .When(query => query.Limit.HasValue)
            .WithErrorCode("validation.invalid_limit")
            .WithMessage($"The limit must be a whole number from 1 to {GetAgentAttemptInputsQuery.MaximumLimit}.");
    }
}
