using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.SetCodexAssignmentPreference;

public sealed class SetCodexAssignmentPreferenceCommandValidator : AbstractValidator<SetCodexAssignmentPreferenceCommand>
{
    public SetCodexAssignmentPreferenceCommandValidator()
    {
        RuleFor(command => command.RequestedModel)
            .MaximumLength(128)
            .When(command => command.RequestedModel is not null)
            .WithErrorCode("validation.invalid");

        RuleFor(command => command.RequestedEffort)
            .MaximumLength(128)
            .When(command => command.RequestedEffort is not null)
            .WithErrorCode("validation.invalid");

        RuleFor(command => command.RequestedEffort)
            .Null()
            .When(command => command.RequestedModel is null)
            .WithErrorCode("validation.invalid")
            .WithMessage("A requested Codex effort requires a requested model.");
    }
}
