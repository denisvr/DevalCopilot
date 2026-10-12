using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordGitHubObservationTarget;

public sealed class RecordGitHubObservationTargetCommandValidator : AbstractValidator<RecordGitHubObservationTargetCommand>
{
    public RecordGitHubObservationTargetCommandValidator()
    {
        RuleFor(command => command.RunId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A run identifier is required.");
        RuleFor(command => command.OperationId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("An operation identifier is required.");
        RuleFor(command => command.CommitSha).Must(GitHubObservationTarget.IsCommitSha).WithErrorCode("validation.invalid")
            .WithMessage("The commit must be exactly 40 lowercase hexadecimal characters.");
        RuleFor(command => command.Owner).Must(GitHubRepositoryNamePolicy.IsOwner).WithErrorCode("validation.invalid")
            .WithMessage("The owner must use 1 to 39 ASCII letters, digits and inner hyphens.");
        RuleFor(command => command.Repository).Must(GitHubRepositoryNamePolicy.IsRepository).WithErrorCode("validation.invalid")
            .WithMessage("The repository must use 1 to 100 ASCII letters, digits, dots, underscores and hyphens.");
    }
}
