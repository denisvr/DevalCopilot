using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateManualRun;

public sealed class CreateManualRunCommandValidator : AbstractValidator<CreateManualRunCommand>
{
    public CreateManualRunCommandValidator()
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
