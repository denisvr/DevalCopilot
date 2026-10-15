using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordGitHubBranchPublicationApproval;

public sealed class RecordGitHubBranchPublicationApprovalCommandValidator
    : AbstractValidator<RecordGitHubBranchPublicationApprovalCommand>
{
    public RecordGitHubBranchPublicationApprovalCommandValidator()
    {
        RuleFor(command => command.RunId).NotEqual(Guid.Empty)
            .WithErrorCode(GitHubBranchPublicationApprovalErrors.InvalidRequestCode).WithMessage("A run identifier is required.");
        RuleFor(command => command.ApprovalId).NotEqual(Guid.Empty)
            .WithErrorCode(GitHubBranchPublicationApprovalErrors.InvalidRequestCode).WithMessage("An approval identifier is required.");
        RuleFor(command => command.OperationId).NotEqual(Guid.Empty)
            .WithErrorCode(GitHubBranchPublicationApprovalErrors.InvalidRequestCode).WithMessage("An operation identifier is required.");
        RuleFor(command => command.CommitSha).Must(GitHubObservationTarget.IsCommitSha)
            .WithErrorCode(GitHubBranchPublicationApprovalErrors.InvalidRequestCode)
            .WithMessage("The commit must be exactly 40 lowercase hexadecimal characters.");
        RuleFor(command => command.BranchName)
            .Must(branch => !string.IsNullOrEmpty(branch) && branch.Length <= GitHubObservationTarget.MaximumBranchNameLength)
            .WithErrorCode(GitHubBranchPublicationApprovalErrors.InvalidRequestCode)
            .WithMessage("The branch must be present and bounded.");
        RuleFor(command => command.TargetId).NotEqual(Guid.Empty)
            .WithErrorCode(GitHubBranchPublicationApprovalErrors.InvalidRequestCode).WithMessage("A target identifier is required.");
        RuleFor(command => command.BindingId).NotEqual(Guid.Empty)
            .WithErrorCode(GitHubBranchPublicationApprovalErrors.InvalidRequestCode).WithMessage("A binding identifier is required.");
        RuleFor(command => command.RepositoryNodeId).Must(GitHubRepositoryNodeIdPolicy.IsNodeId)
            .WithErrorCode(GitHubBranchPublicationApprovalErrors.InvalidRequestCode)
            .WithMessage("The repository node identifier must use 1 to 256 ASCII letters, digits and the characters _ - / + =.");
    }
}
