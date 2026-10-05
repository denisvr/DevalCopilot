using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.SetCodexAccountUsageWarning;

public sealed class SetCodexAccountUsageWarningCommandValidator : AbstractValidator<SetCodexAccountUsageWarningCommand>
{
    public SetCodexAccountUsageWarningCommandValidator()
    {
        RuleFor(command => command.Percent!.Value)
            .InclusiveBetween(CodexAccountUsageWarning.Minimum, CodexAccountUsageWarning.Maximum)
            .When(command => command.Percent is not null)
            .WithName(nameof(SetCodexAccountUsageWarningCommand.Percent))
            .WithErrorCode("validation.invalid")
            .WithMessage("The account-usage warning must be a whole number from 1 through 100.");
    }
}
