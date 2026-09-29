using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.SetTokenStopThreshold;

public sealed class SetTokenStopThresholdCommandValidator : AbstractValidator<SetTokenStopThresholdCommand>
{
    public SetTokenStopThresholdCommandValidator()
    {
        RuleFor(command => command.Provider)
            .Must(provider => TokenStopProviders.TryParse(provider, out _))
            .WithErrorCode("validation.invalid")
            .WithMessage("The provider must be Codex or ClaudeCode.");

        RuleFor(command => command.ThresholdTokens!.Value)
            .InclusiveBetween(1, Run.MaxTokenStopThreshold)
            .When(command => command.ThresholdTokens is not null)
            .WithName(nameof(SetTokenStopThresholdCommand.ThresholdTokens))
            .WithErrorCode("validation.invalid")
            .WithMessage("The threshold must be positive and within the supported maximum.");
    }
}
