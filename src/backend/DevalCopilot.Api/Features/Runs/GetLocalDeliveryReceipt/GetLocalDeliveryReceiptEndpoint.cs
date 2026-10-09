using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalDeliveryReceipt;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetLocalDeliveryReceipt;

/// <summary>The recorded, read-only historical receipt of a run's completed local delivery (ADR-0032). An unknown run is a safe 404.
/// It reads persisted facts only and exposes no path, argument, author address, message text, provider payload or artifact.</summary>
public sealed class GetLocalDeliveryReceiptEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/local-delivery-receipt")]
    public async Task<ActionResult<GetLocalDeliveryReceiptResponse>> GetLocalDeliveryReceipt(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetLocalDeliveryReceiptQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetLocalDeliveryReceiptResponse(value.State.ToString(), value.Receipt is null ? null : Map(value.Receipt)));
    }

    private static LocalDeliveryReceiptResponse Map(LocalDeliveryReceiptView receipt) => new(
        receipt.Version,
        receipt.RunId,
        receipt.OperationId,
        receipt.Objective,
        receipt.CommitSha,
        receipt.ParentCommitSha,
        receipt.TreeSha,
        receipt.BranchName,
        receipt.CompletedAtUtc,
        new LocalDeliveryCheckpointResponse(
            receipt.Checkpoint.Id, receipt.Checkpoint.Number, receipt.Checkpoint.FingerprintSha256, receipt.Checkpoint.ChangedPathCount),
        receipt.ExecutionReportMessageId,
        new LocalDeliveryCodeReviewResponse(
            receipt.CodeReview.AttemptId, receipt.CodeReview.AttemptNumber, receipt.CodeReview.ApprovalMessageId),
        new LocalDeliveryHumanReviewResponse(receipt.HumanReview.ReviewId, receipt.HumanReview.Decision),
        receipt.Verification
            .Select(member => new LocalDeliveryVerificationResponse(
                member.Order, member.CommandId, member.ExecutionId, member.ExecutionNumber, member.CommandName, member.Status,
                member.ExitCode, member.CompletedAtUtc))
            .ToArray());
}
