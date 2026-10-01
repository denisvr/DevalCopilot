using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;

/// <summary>Bounds optional direct guidance before the handler runs. Null is the unguided request. The message is
/// fixed and never carries the submitted text.</summary>
public sealed class CreateReviewCorrectionAttemptCommandValidator : AbstractValidator<CreateReviewCorrectionAttemptCommand>
{
    public CreateReviewCorrectionAttemptCommandValidator()
    {
        RuleFor(command => command.Guidance)
            .Must(guidance => DirectHumanGuidance.Normalize(guidance) is not null)
            .When(command => command.Guidance is not null)
            .WithErrorCode(DirectHumanGuidanceErrors.InvalidCode)
            .WithMessage(DirectHumanGuidanceErrors.InvalidMessage);
    }
}
