using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.AbandonManualRun;

public sealed class AbandonManualRunCommandValidator : AbstractValidator<AbandonManualRunCommand>
{
    public AbandonManualRunCommandValidator()
    {
        RuleFor(command => command.RunId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A run identifier is required.");
        RuleFor(command => command.Reason)
            .Must(reason => RunAbandonmentPolicy.TryNormalizeReason(reason, out _))
            .WithErrorCode("validation.invalid")
            .WithMessage("The reason needs text, no control characters other than line feeds, and at most 2 KiB.");
    }
}
