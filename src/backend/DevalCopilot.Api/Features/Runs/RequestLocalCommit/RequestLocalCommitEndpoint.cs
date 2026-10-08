using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.RequestLocalCommit;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RequestLocalCommit;

/// <summary>
/// Admits one explicit local commit of a run's exact verified, Agent-reviewed and human-approved checkpoint (ADR-0029). The host
/// performs the Git work later, locally, unsigned and without hooks; nothing is pushed. A replay of the same operation and request
/// returns the recorded operation; anything else is a conflict. The request body is bounded to 8 KiB.
/// </summary>
public sealed class RequestLocalCommitEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = 8 * 1024;

    [HttpPost("{runId:guid}/local-commit")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    public async Task<ActionResult<LocalCommitOperationResponse>> RequestLocalCommit(
        [FromRoute] Guid runId, [FromBody] RequestLocalCommitRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new RequestLocalCommitCommand(
                runId, request.OperationId, request.CheckpointId, request.CodeReviewAttemptId,
                request.HumanCheckpointReviewId, request.Message),
            cancellationToken);

        return result.IsFailure ? problemDetails.CreateResponse(result, HttpContext) : Ok(Map(result.Value));
    }

    internal static LocalCommitOperationResponse Map(LocalCommitOperationView view) => new(
        view.OperationId,
        view.Status,
        view.OutcomeReasonCode,
        view.CheckpointId,
        view.CheckpointNumber,
        view.CodeReviewAttemptId,
        view.HumanCheckpointReviewId,
        view.BranchName,
        view.ParentCommitSha,
        view.TreeSha,
        view.CommitSha,
        view.ChangedPathCount,
        view.CreatedAtUtc,
        view.CompletedAtUtc);
}
