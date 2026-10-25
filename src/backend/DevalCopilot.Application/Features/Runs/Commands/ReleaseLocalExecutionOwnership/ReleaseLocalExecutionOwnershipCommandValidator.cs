using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.ReleaseLocalExecutionOwnership;

public sealed class ReleaseLocalExecutionOwnershipCommandValidator : AbstractValidator<ReleaseLocalExecutionOwnershipCommand>
{
    public ReleaseLocalExecutionOwnershipCommandValidator()
    {
        RuleFor(command => command.RunId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A run identifier is required.");
        RuleFor(command => command.StepId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A step identifier is required.");
        RuleFor(command => command.ObservationId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("An observation identifier is required.");
        RuleFor(command => command.ConfirmationVersion).Equal(LocalExecutionOwnershipReleaseConfirmation.CurrentVersion)
            .WithErrorCode("validation.invalid")
            .WithMessage("The confirmation version is not supported.");
        RuleFor(command => command.ConfirmationSha256)
            .Must(LocalExecutionSequencePolicy.IsLowercaseSha256)
            .WithErrorCode("validation.invalid")
            .WithMessage("The confirmation must be 64 lowercase hexadecimal characters.");
        RuleFor(command => command.ConfirmationExpiresAtUtc)
            .Must(expiry => expiry.Offset == TimeSpan.Zero)
            .WithErrorCode("validation.invalid")
            .WithMessage("The confirmation expiry must be a UTC time.");
    }
}
