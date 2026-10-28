using DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalCommitInspection;

/// <summary>Structural bound only: a run identity must be nonempty. The failure carries fixed text and never repeats the value.</summary>
public sealed class GetLocalCommitInspectionQueryValidator : AbstractValidator<GetLocalCommitInspectionQuery>
{
    public GetLocalCommitInspectionQueryValidator()
    {
        RuleFor(query => query.RunId)
            .NotEqual(Guid.Empty)
            .WithErrorCode(LocalCommitInspectionErrors.InvalidRunCode)
            .WithMessage(LocalCommitInspectionErrors.InvalidRunMessage);
    }
}
