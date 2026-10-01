using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.AuthorizePlanningImplementation;

/// <summary>Bounds the required rationale before the handler runs. The message is fixed and never carries the
/// submitted text.</summary>
public sealed class AuthorizePlanningImplementationCommandValidator : AbstractValidator<AuthorizePlanningImplementationCommand>
{
    public AuthorizePlanningImplementationCommandValidator()
    {
        RuleFor(command => command.Rationale)
            .Must(rationale => PlanningImplementationInstruction.Normalize(rationale) is not null)
            .WithErrorCode(PlanningImplementationAuthorizationErrors.RationaleInvalidCode)
            .WithMessage(PlanningImplementationAuthorizationErrors.RationaleInvalidMessage);
    }
}
