using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.SetExecutionCapacityLimit;

public sealed class SetExecutionCapacityLimitCommandValidator : AbstractValidator<SetExecutionCapacityLimitCommand>
{
    public SetExecutionCapacityLimitCommandValidator()
    {
        RuleFor(command => command.Limit)
            .Must(ExecutionCapacityConfigurationRevision.IsSupportedLimit)
            .WithErrorCode("validation.invalid")
            .WithMessage("The limit must be one or two.");
    }
}
