using FluentValidation;

namespace DevalCopilot.Application.Features.Projects.Commands.PrepareRepositoryWorkspace;

public sealed class PrepareRepositoryWorkspaceCommandValidator : AbstractValidator<PrepareRepositoryWorkspaceCommand>
{
    public PrepareRepositoryWorkspaceCommandValidator()
    {
        RuleFor(command => command.ProjectId).NotEmpty().WithErrorCode("validation.required");
    }
}
