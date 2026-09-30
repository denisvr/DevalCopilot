using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.SetClaudeMutationTurnLimit;

public sealed class SetClaudeMutationTurnLimitCommandValidator : AbstractValidator<SetClaudeMutationTurnLimitCommand>
{
    public SetClaudeMutationTurnLimitCommandValidator()
    {
        RuleFor(command => command.MaxTurns!.Value)
            .InclusiveBetween(ClaudeMutationTurnLimit.Minimum, ClaudeMutationTurnLimit.Maximum)
            .When(command => command.MaxTurns is not null)
            .WithName(nameof(SetClaudeMutationTurnLimitCommand.MaxTurns))
            .WithErrorCode("validation.invalid")
            .WithMessage("The turn limit must be a whole number from 1 through 100.");
    }
}
