using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageStop;

public sealed class SetCodexAccountUsageStopCommandValidator : AbstractValidator<SetCodexAccountUsageStopCommand>
{
    public SetCodexAccountUsageStopCommandValidator()
    {
        RuleFor(command => command.Percent!.Value)
            .InclusiveBetween(CodexAccountUsageStop.Minimum, CodexAccountUsageStop.Maximum)
            .When(command => command.Percent is not null)
            .WithName(nameof(SetCodexAccountUsageStopCommand.Percent))
            .WithErrorCode("validation.invalid")
            .WithMessage("The account-usage stop must be a whole number from 1 through 100.");
    }
}
