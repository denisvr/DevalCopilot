using FluentValidation;

namespace DevalCopilot.Application.Features.Projects.Commands.RegisterProject;

/// <summary>
/// Structural checks only — presence and length. Whether the path is absolute, exists, is a
/// reparse point, is a UNC root, or is a real Git repository are all business rules requiring
/// filesystem/process I/O, and belong in <see cref="RegisterProjectCommandHandler"/> via its
/// ports, never here. This validator never references <c>System.IO</c> or <c>Path</c>.
/// </summary>
public sealed class RegisterProjectCommandValidator : AbstractValidator<RegisterProjectCommand>
{
    public RegisterProjectCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(200)
            .WithErrorCode("validation.required");

        RuleFor(command => command.RequestedPath)
            .NotEmpty()
            .MaximumLength(1000)
            .WithErrorCode("validation.required");
    }
}
