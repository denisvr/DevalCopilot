using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateReviewCorrectionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.ClaimLocalExecutionSequenceStep;

public sealed class ClaimLocalExecutionSequenceStepCommandHandler(IDevalCopilotDbContext dbContext, IApplicationMediator mediator)
    : ICommandHandler<ClaimLocalExecutionSequenceStepCommand, Result<ClaimLocalExecutionSequenceStepCommandResult>>
{
    public async Task<Result<ClaimLocalExecutionSequenceStepCommandResult>> HandleAsync(
        ClaimLocalExecutionSequenceStepCommand command, CancellationToken cancellationToken)
    {
        var ticket = command.Ticket;

        // Only identities are read here, untracked; the composed claim decides everything else from its own fresh reads and re-checks the
        // exact step at its durable seam. Nothing here is caller-supplied.
        var sequence = await dbContext.LocalExecutionSequences.AsNoTracking()
            .Where(candidate => candidate.Id == ticket.SequenceId)
            .Select(candidate => new { candidate.RunId, candidate.ProjectId, candidate.PlanProposalMessageId })
            .SingleOrDefaultAsync(cancellationToken);
        if (sequence is null)
        {
            return Failure(LocalExecutionSequenceErrors.StepAuthorityLost());
        }

        switch (ticket.Kind)
        {
            case LocalExecutionStepKind.Implementation:
                {
                    var claimed = await mediator.SendAsync(
                        new CreateImplementationAttemptCommand(sequence.RunId, sequence.PlanProposalMessageId) { LocalStep = ticket },
                        cancellationToken);
                    return claimed.IsFailure ? Failure(claimed.Errors) : Success(claimed.Value.AttemptId);
                }

            case LocalExecutionStepKind.Verification:
                {
                    var commandId = await ResolveRecipeCommandIdAsync(ticket, cancellationToken);
                    if (commandId is not { } recipe)
                    {
                        return Failure(LocalExecutionSequenceErrors.StepAuthorityLost());
                    }

                    var verified = await mediator.SendAsync(
                        new ClaimVerificationExecutionCommand(sequence.ProjectId, recipe, ticket.SourceCheckpointId) { LocalStep = ticket },
                        cancellationToken);
                    return verified.IsFailure ? Failure(verified.Errors) : Success(verified.Value.VerificationExecutionId);
                }

            case LocalExecutionStepKind.CodeReview when ticket.ReportMessageId is { } reviewedReport:
                {
                    var reviewed = await mediator.SendAsync(
                        new CreateCodeReviewAttemptCommand(sequence.RunId, reviewedReport) { LocalStep = ticket }, cancellationToken);
                    return reviewed.IsFailure ? Failure(reviewed.Errors) : Success(reviewed.Value.AttemptId);
                }

            case LocalExecutionStepKind.VerificationDiagnosis when ticket.ReportMessageId is { } diagnosedReport:
                {
                    var diagnosed = await mediator.SendAsync(
                        new CreateVerificationDiagnosisAttemptCommand(sequence.RunId, diagnosedReport) { LocalStep = ticket },
                        cancellationToken);
                    return diagnosed.IsFailure ? Failure(diagnosed.Errors) : Success(diagnosed.Value.AttemptId);
                }

            case LocalExecutionStepKind.ReviewCorrection when ticket.SubjectAttemptId is { } reviewAttempt:
                {
                    var corrected = await mediator.SendAsync(
                        new CreateReviewCorrectionAttemptCommand(sequence.RunId, reviewAttempt) { LocalStep = ticket }, cancellationToken);
                    return corrected.IsFailure
                        ? Failure(corrected.Errors)
                        : corrected.Value is CreateReviewCorrectionAttemptCommandResult.AttemptCreated created
                            ? Success(created.AttemptId)
                            : Failure(LocalExecutionSequenceErrors.AllowanceReached());
                }

            case LocalExecutionStepKind.DiagnosisCorrection when ticket.SubjectAttemptId is { } diagnosisAttempt:
                {
                    var corrected = await mediator.SendAsync(
                        new CreateDiagnosisCorrectionAttemptCommand(sequence.RunId, diagnosisAttempt) { LocalStep = ticket },
                        cancellationToken);
                    return corrected.IsFailure
                        ? Failure(corrected.Errors)
                        : corrected.Value is CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated created
                            ? Success(created.AttemptId)
                            : Failure(LocalExecutionSequenceErrors.AllowanceReached());
                }

            default:
                return Failure(LocalExecutionSequenceErrors.StepAuthorityLost());
        }
    }

    /// <summary>The project's verification command pinned at the step's recipe position; the composed claim re-checks it by value.</summary>
    private async Task<Guid?> ResolveRecipeCommandIdAsync(
        DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences.LocalExecutionStepTicket ticket,
        CancellationToken cancellationToken)
    {
        var recipeOrdinal = await dbContext.LocalExecutionSequenceSteps.AsNoTracking()
            .Where(step => step.Id == ticket.StepId)
            .Select(step => step.RecipeOrdinal)
            .SingleOrDefaultAsync(cancellationToken);
        return recipeOrdinal is not { } ordinal
            ? null
            : await dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
                .Where(recipe => recipe.SequenceId == ticket.SequenceId && recipe.Ordinal == ordinal)
                .Select(recipe => (Guid?)recipe.VerificationCommandId)
                .SingleOrDefaultAsync(cancellationToken);
    }

    private static Result<ClaimLocalExecutionSequenceStepCommandResult> Success(Guid operationId) =>
        Result<ClaimLocalExecutionSequenceStepCommandResult>.Success(new ClaimLocalExecutionSequenceStepCommandResult(operationId));

    private static Result<ClaimLocalExecutionSequenceStepCommandResult> Failure(Error error) =>
        Result<ClaimLocalExecutionSequenceStepCommandResult>.Failure(error);

    private static Result<ClaimLocalExecutionSequenceStepCommandResult> Failure(IReadOnlyList<Error> errors) =>
        Result<ClaimLocalExecutionSequenceStepCommandResult>.Failure(errors);
}
