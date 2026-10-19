using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.ArmPlanningSequence;

public sealed class ArmPlanningSequenceCommandValidator : AbstractValidator<ArmPlanningSequenceCommand>
{
    public ArmPlanningSequenceCommandValidator()
    {
        RuleFor(command => command.RunId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A run identifier is required.");
        RuleFor(command => command.WorkspaceId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A workspace identifier is required.");
        RuleFor(command => command.CheckpointId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A checkpoint identifier is required.");
        RuleFor(command => command.FingerprintSha256)
            .Must(PlanningSequencePolicy.IsLowercaseSha256)
            .WithErrorCode("validation.invalid")
            .WithMessage("The fingerprint must be 64 lowercase hexadecimal characters.");
    }
}
