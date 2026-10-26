using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;
using FluentValidation;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationOutputWindow;

/// <summary>Structural bounds only. An offset, cap or stream outside them is a validation failure: nothing is clamped or defaulted for it.
/// The messages are fixed text and never repeat the rejected value.</summary>
public sealed class GetProjectVerificationOutputWindowQueryValidator : AbstractValidator<GetProjectVerificationOutputWindowQuery>
{
    public GetProjectVerificationOutputWindowQueryValidator()
    {
        RuleFor(query => query.FromOffset)
            .InclusiveBetween(0, GetProjectVerificationOutputWindowQuery.MaximumOffset)
            .WithErrorCode(ProjectVerificationInspectionErrors.InvalidOffsetCode)
            .WithMessage(ProjectVerificationInspectionErrors.InvalidOffsetMessage);

        RuleFor(query => query.MaxBytes)
            .InclusiveBetween(GetProjectVerificationOutputWindowQuery.MinimumMaxBytes, GetProjectVerificationOutputWindowQuery.MaximumMaxBytes)
            .WithErrorCode(ProjectVerificationInspectionErrors.InvalidMaxBytesCode)
            .WithMessage(ProjectVerificationInspectionErrors.InvalidMaxBytesMessage);

        RuleFor(query => query.Stream)
            .IsInEnum()
            .WithErrorCode(ProjectVerificationInspectionErrors.InvalidStreamCode)
            .WithMessage("The stream must be standard output or standard error.");
    }
}
