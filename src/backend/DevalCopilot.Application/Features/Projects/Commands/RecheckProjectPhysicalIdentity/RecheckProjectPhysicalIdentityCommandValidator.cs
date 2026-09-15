using FluentValidation;

namespace DevalCopilot.Application.Features.Projects.Commands.RecheckProjectPhysicalIdentity;

public sealed class RecheckProjectPhysicalIdentityCommandValidator : AbstractValidator<RecheckProjectPhysicalIdentityCommand>
{
    public RecheckProjectPhysicalIdentityCommandValidator()
    {
        RuleFor(command => command.ProjectId).NotEmpty().WithErrorCode("validation.required");
    }
}
