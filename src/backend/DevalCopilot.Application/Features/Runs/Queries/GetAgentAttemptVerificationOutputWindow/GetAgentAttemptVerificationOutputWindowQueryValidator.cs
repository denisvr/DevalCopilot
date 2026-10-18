using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationOutputWindow;

/// <summary>Structural bounds only. An offset, cap or stream outside them is a validation failure: nothing is clamped or defaulted for
/// it. The messages are fixed text and never repeat the rejected value.</summary>
public sealed class GetAgentAttemptVerificationOutputWindowQueryValidator : AbstractValidator<GetAgentAttemptVerificationOutputWindowQuery>
{
    public GetAgentAttemptVerificationOutputWindowQueryValidator()
    {
        RuleFor(query => query.FromOffset)
            .InclusiveBetween(0, GetAgentAttemptVerificationOutputWindowQuery.MaximumOffset)
            .WithErrorCode(GetAgentAttemptVerificationOutputWindowErrors.InvalidOffsetCode)
            .WithMessage(GetAgentAttemptVerificationOutputWindowErrors.InvalidOffsetMessage);

        RuleFor(query => query.MaxBytes)
            .InclusiveBetween(
                GetAgentAttemptVerificationOutputWindowQuery.MinimumMaxBytes, GetAgentAttemptVerificationOutputWindowQuery.MaximumMaxBytes)
            .WithErrorCode(GetAgentAttemptVerificationOutputWindowErrors.InvalidMaxBytesCode)
            .WithMessage(GetAgentAttemptVerificationOutputWindowErrors.InvalidMaxBytesMessage);

        RuleFor(query => query.Stream)
            .IsInEnum()
            .WithErrorCode(GetAgentAttemptVerificationOutputWindowErrors.InvalidStreamCode)
            .WithMessage("The stream must be standard output or standard error.");
    }
}
