using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.StopPlanningSequence;

public sealed class StopPlanningSequenceCommandValidator : AbstractValidator<StopPlanningSequenceCommand>
{
    public StopPlanningSequenceCommandValidator()
    {
        RuleFor(command => command.RunId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A run identifier is required.");
        RuleFor(command => command.SequenceId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A sequence identifier is required.");
    }
}
