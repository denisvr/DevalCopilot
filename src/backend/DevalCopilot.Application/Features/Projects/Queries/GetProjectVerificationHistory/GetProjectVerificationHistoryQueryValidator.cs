using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;
using FluentValidation;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationHistory;

/// <summary>Structural bounds only. A cursor outside them is a validation failure with fixed text that never repeats the rejected value.</summary>
public sealed class GetProjectVerificationHistoryQueryValidator : AbstractValidator<GetProjectVerificationHistoryQuery>
{
    public GetProjectVerificationHistoryQueryValidator()
    {
        RuleFor(query => query.BeforeExecutionNumber)
            .GreaterThanOrEqualTo(1)
            .When(query => query.BeforeExecutionNumber is not null)
            .WithErrorCode(ProjectVerificationInspectionErrors.InvalidBeforeExecutionNumberCode)
            .WithMessage(ProjectVerificationInspectionErrors.InvalidBeforeExecutionNumberMessage);
    }
}
