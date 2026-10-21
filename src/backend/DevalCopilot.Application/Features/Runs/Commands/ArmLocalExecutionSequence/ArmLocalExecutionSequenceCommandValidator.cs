using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.ArmLocalExecutionSequence;

public sealed class ArmLocalExecutionSequenceCommandValidator : AbstractValidator<ArmLocalExecutionSequenceCommand>
{
    public ArmLocalExecutionSequenceCommandValidator()
    {
        RuleFor(command => command.RunId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A run identifier is required.");
        RuleFor(command => command.PlanProposalMessageId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A plan identifier is required.");
        RuleFor(command => command.WorkspaceId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A workspace identifier is required.");
        RuleFor(command => command.CheckpointId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A checkpoint identifier is required.");
        RuleFor(command => command.FingerprintSha256)
            .Must(LocalExecutionSequencePolicy.IsLowercaseSha256)
            .WithErrorCode("validation.invalid")
            .WithMessage("The fingerprint must be 64 lowercase hexadecimal characters.");
        RuleFor(command => command.ConfirmationSha256)
            .Must(LocalExecutionSequencePolicy.IsLowercaseSha256)
            .WithErrorCode("validation.invalid")
            .WithMessage("The confirmation must be 64 lowercase hexadecimal characters.");
    }
}
