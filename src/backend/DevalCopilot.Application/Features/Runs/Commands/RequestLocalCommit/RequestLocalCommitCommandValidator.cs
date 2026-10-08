using DevalCopilot.Domain.Features.Runs;
using FluentValidation;

namespace DevalCopilot.Application.Features.Runs.Commands.RequestLocalCommit;

public sealed class RequestLocalCommitCommandValidator : AbstractValidator<RequestLocalCommitCommand>
{
    public RequestLocalCommitCommandValidator()
    {
        RuleFor(command => command.OperationId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("An operation identifier is required.");
        RuleFor(command => command.CheckpointId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("A checkpoint identifier is required.");
        RuleFor(command => command.CodeReviewAttemptId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("An approved CodeReviewer attempt identifier is required.");
        RuleFor(command => command.HumanCheckpointReviewId).NotEqual(Guid.Empty).WithErrorCode("validation.invalid")
            .WithMessage("An approved human checkpoint review identifier is required.");
        RuleFor(command => command.Message)
            .Must(message => LocalCommitMessagePolicy.TryNormalize(message, out _))
            .WithErrorCode("validation.invalid")
            .WithMessage("The commit message needs a subject, LF line endings, no control characters and at most 2 KiB.");
    }
}
