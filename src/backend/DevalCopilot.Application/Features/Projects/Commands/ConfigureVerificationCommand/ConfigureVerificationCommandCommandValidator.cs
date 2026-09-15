using DevalCopilot.Domain.Features.Projects;
using FluentValidation;

namespace DevalCopilot.Application.Features.Projects.Commands.ConfigureVerificationCommand;

public sealed class ConfigureVerificationCommandCommandValidator : AbstractValidator<ConfigureVerificationCommandCommand>
{
    public ConfigureVerificationCommandCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200).WithErrorCode("validation.required");
        RuleFor(command => command.ExecutablePath).NotEmpty().MaximumLength(1024).WithErrorCode("validation.required");
        RuleFor(command => command.Arguments).NotNull().Must(arguments => arguments.Count <= VerificationCommand.MaximumArgumentCount)
            .WithErrorCode("validation.invalid");
        RuleForEach(command => command.Arguments).NotNull()
            .Must(argument => System.Text.Encoding.UTF8.GetByteCount(argument) <= VerificationCommand.MaximumArgumentUtf8Bytes)
            .WithErrorCode("validation.invalid");
        RuleFor(command => command.TimeoutSeconds).InclusiveBetween(1, VerificationCommand.MaximumTimeoutSeconds)
            .WithErrorCode("validation.invalid");
    }
}
