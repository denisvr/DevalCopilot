using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.StopLocalExecutionSequence;

public sealed class StopLocalExecutionSequenceCommandValidator : AbstractValidator<StopLocalExecutionSequenceCommand>
{
    public StopLocalExecutionSequenceCommandValidator()
    {
        RuleFor(command => command.RunId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A run identifier is required.");
        RuleFor(command => command.SequenceId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A sequence identifier is required.");
    }
}
