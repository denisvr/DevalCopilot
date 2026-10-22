using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionVerificationOutputWindow;

/// <summary>Structural bounds only. An offset or cap outside them is a validation failure: nothing is clamped or defaulted for it. The
/// messages are fixed text and never repeat the rejected value. A stream outside the two supported ones is the handler's fixed 404.</summary>
public sealed class GetLocalExecutionVerificationOutputWindowQueryValidator
    : AbstractValidator<GetLocalExecutionVerificationOutputWindowQuery>
{
    public GetLocalExecutionVerificationOutputWindowQueryValidator()
    {
        RuleFor(query => query.FromOffset)
            .InclusiveBetween(0, GetLocalExecutionVerificationOutputWindowQuery.MaximumOffset)
            .WithErrorCode(GetLocalExecutionVerificationOutputWindowErrors.InvalidOffsetCode)
            .WithMessage(GetLocalExecutionVerificationOutputWindowErrors.InvalidOffsetMessage);

        RuleFor(query => query.MaxBytes)
            .InclusiveBetween(
                GetLocalExecutionVerificationOutputWindowQuery.MinimumMaxBytes,
                GetLocalExecutionVerificationOutputWindowQuery.MaximumMaxBytes)
            .WithErrorCode(GetLocalExecutionVerificationOutputWindowErrors.InvalidMaxBytesCode)
            .WithMessage(GetLocalExecutionVerificationOutputWindowErrors.InvalidMaxBytesMessage);
    }
}
