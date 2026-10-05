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

        RuleFor(command => command.MaximumAgentAttempts!.Value)
            .InclusiveBetween(ManualRunBudgetRange.MinimumAgentAttempts, ManualRunBudgetRange.MaximumAgentAttempts)
            .When(command => command.MaximumAgentAttempts is not null)
            .WithName(nameof(CreateManualRunCommand.MaximumAgentAttempts))
            .WithErrorCode("validation.invalid")
            .WithMessage("The Agent claim ceiling must be a whole number from 1 through 16.");

        RuleFor(command => command.MaximumAgentInvocationMinutes!.Value)
            .InclusiveBetween(ManualRunBudgetRange.MinimumAgentInvocationMinutes, ManualRunBudgetRange.MaximumAgentInvocationMinutes)
            .When(command => command.MaximumAgentInvocationMinutes is not null)
            .WithName(nameof(CreateManualRunCommand.MaximumAgentInvocationMinutes))
            .WithErrorCode("validation.invalid")
            .WithMessage("The reserved invocation time must be a whole number of minutes from 1 through 120.");
    }
}
