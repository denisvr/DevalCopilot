using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.BindGitHubRepositoryIdentity;

public sealed class BindGitHubRepositoryIdentityCommandValidator : AbstractValidator<BindGitHubRepositoryIdentityCommand>
{
    public BindGitHubRepositoryIdentityCommandValidator()
    {
        RuleFor(command => command.RunId).NotEqual(Guid.Empty).WithErrorCode(GitHubRepositoryIdentityBindingErrors.InvalidCode)
            .WithMessage("A run identifier is required.");
        RuleFor(command => command.OperationId).NotEqual(Guid.Empty).WithErrorCode(GitHubRepositoryIdentityBindingErrors.InvalidCode)
            .WithMessage("An operation identifier is required.");
        RuleFor(command => command.CommitSha).Must(GitHubObservationTarget.IsCommitSha)
            .WithErrorCode(GitHubRepositoryIdentityBindingErrors.InvalidCode)
            .WithMessage("The commit must be exactly 40 lowercase hexadecimal characters.");
        RuleFor(command => command.TargetId).NotEqual(Guid.Empty).WithErrorCode(GitHubRepositoryIdentityBindingErrors.InvalidCode)
            .WithMessage("A target identifier is required.");
        RuleFor(command => command.RepositoryNodeId).Must(GitHubRepositoryNodeIdPolicy.IsNodeId)
            .WithErrorCode(GitHubRepositoryIdentityBindingErrors.InvalidCode)
            .WithMessage("The repository node identifier must use 1 to 256 ASCII letters, digits and the characters _ - / + =.");
    }
}
