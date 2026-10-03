using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;

/// <summary>Bounds optional direct guidance before the handler runs, with the one shared policy and fixed message of ADR-0015.
/// Null is the unguided request. The message never carries the submitted text.</summary>
public sealed class CreateDiagnosisCorrectionAttemptCommandValidator : AbstractValidator<CreateDiagnosisCorrectionAttemptCommand>
{
    public CreateDiagnosisCorrectionAttemptCommandValidator()
    {
        RuleFor(command => command.Guidance)
            .Must(guidance => DirectHumanGuidance.Normalize(guidance) is not null)
            .When(command => command.Guidance is not null)
            .WithErrorCode(DirectHumanGuidanceErrors.InvalidCode)
            .WithMessage(DirectHumanGuidanceErrors.InvalidMessage);
    }
}
