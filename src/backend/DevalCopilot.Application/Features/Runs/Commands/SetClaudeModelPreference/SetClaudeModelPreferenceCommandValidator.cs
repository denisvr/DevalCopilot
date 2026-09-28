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
    }
}
