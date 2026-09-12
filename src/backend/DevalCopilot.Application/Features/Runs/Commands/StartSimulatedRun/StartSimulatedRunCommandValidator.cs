using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;

public sealed class StartSimulatedRunCommandValidator : AbstractValidator<StartSimulatedRunCommand>
{
    public StartSimulatedRunCommandValidator()
    {
        RuleFor(command => command.ProjectId)
            .NotEmpty()
            .WithErrorCode("validation.required");

        RuleFor(command => command.Objective)
            .NotEmpty()
            .MaximumLength(2000)
            .WithErrorCode("validation.required");
    }
}
