using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.AuthorizeReviewCorrection;

/// <summary>Bounds optional guidance before the handler runs. A null value is the bodyless
/// authorization. The message is fixed and never carries the submitted text.</summary>
public sealed class AuthorizeReviewCorrectionCommandValidator : AbstractValidator<AuthorizeReviewCorrectionCommand>
{
    public AuthorizeReviewCorrectionCommandValidator()
    {
        RuleFor(command => command.Guidance)
            .Must(guidance => ReviewCorrectionGuidance.Normalize(guidance) is not null)
            .When(command => command.Guidance is not null)
            .WithErrorCode("review_correction_authorizations.guidance_invalid")
            .WithMessage(
                $"The guidance must be non-blank text of at most {ReviewCorrectionGuidance.MaximumLength} characters "
                + "without control characters or unsafe content.");
    }
}
