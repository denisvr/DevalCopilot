using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.SetClaudeModelPreference;

public sealed class SetClaudeModelPreferenceCommandValidator : AbstractValidator<SetClaudeModelPreferenceCommand>
{
    public SetClaudeModelPreferenceCommandValidator()
    {
        RuleFor(command => command.RequestedModel)
            .Must(value => ClaudeModelAlias.IsSupported(value))
            .When(command => command.RequestedModel is not null)
            .WithErrorCode("validation.invalid")
            .WithMessage("The requested Claude model must be one of the supported aliases.");

        RuleFor(command => command.RequestedEffort)
            .Must(value => ClaudeEffortLevel.IsSupported(value))
            .When(command => command.RequestedEffort is not null)
            .WithErrorCode("validation.invalid")
            .WithMessage("The requested Claude effort must be one of low, medium, or high.");

        RuleFor(command => command.RequestedEffort)
            .Must((command, _) => ClaudeModelRequest.IsValid(command.RequestedModel, command.RequestedEffort))
            .When(command => command.RequestedEffort is not null && ClaudeEffortLevel.IsSupported(command.RequestedEffort))
            .WithErrorCode("validation.invalid")
            .WithMessage("A requested Claude effort requires an explicitly requested sonnet or opus model.");
    }
}
