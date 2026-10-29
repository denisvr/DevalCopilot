using DevalCopilot.Application.Features.Runs.Policies.RecordedRunAbandonment;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Queries.GetRunAbandonmentInspection;

/// <summary>Structural bound only: a run identity must be nonempty. The failure carries fixed text and never repeats the value.</summary>
public sealed class GetRunAbandonmentInspectionQueryValidator : AbstractValidator<GetRunAbandonmentInspectionQuery>
{
    public GetRunAbandonmentInspectionQueryValidator()
    {
        RuleFor(query => query.RunId)
            .NotEqual(Guid.Empty)
            .WithErrorCode(RunAbandonmentInspectionErrors.InvalidRunCode)
            .WithMessage(RunAbandonmentInspectionErrors.InvalidRunMessage);
    }
}
