using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.SetTokenWarningThreshold;

public sealed class SetTokenWarningThresholdCommandValidator : AbstractValidator<SetTokenWarningThresholdCommand>
{
    public SetTokenWarningThresholdCommandValidator()
    {
        RuleFor(command => command.Provider)
            .Must(provider => TokenWarningProviders.TryParse(provider, out _))
            .WithErrorCode("validation.invalid")
            .WithMessage("The provider must be Codex or ClaudeCode.");

        RuleFor(command => command.ThresholdTokens!.Value)
            .InclusiveBetween(1, Run.MaxTokenWarningThreshold)
            .When(command => command.ThresholdTokens is not null)
            .WithName(nameof(SetTokenWarningThresholdCommand.ThresholdTokens))
            .WithErrorCode("validation.invalid")
            .WithMessage("The threshold must be positive and within the supported maximum.");
    }
}
