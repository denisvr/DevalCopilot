using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetGitHubBranchPublicationApproval;
using DevalCopilot.Application.Features.Runs.Commands.RecordGitHubBranchPublicationApproval;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.RecordGitHubBranchPublicationApproval;

/// <summary>
/// Explicitly records the Human's approval of ONE future normal push of a run's exact recorded local commit to its exact recorded owned
/// branch in the repository named by its coherent ADR-0040 target and ADR-0043 binding (ADR-0048). The body holds exactly seven string
/// members, is bounded to 8 KiB, and a query key, an unknown, duplicate, differently cased or escaped member or an oversized body is
/// refused before anything is dispatched. The host resolves the pinned scope itself, requires every acknowledgement to equal it, and saves
/// the approval and one Human-authored event atomically; nothing external runs. A replay of the same coherent approval returns the
/// original record. The approval is recorded consent only: never readiness, permission, a push, a pull request or automatic execution.
/// </summary>
public sealed class RecordGitHubBranchPublicationApprovalEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = 8 * 1024;

    [HttpPost("{runId:guid}/github-branch-publication-approval")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    [TypeFilter(typeof(StrictPublicationApprovalBodyFilter))]
    [ProducesResponseType<RecordGitHubBranchPublicationApprovalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<RecordGitHubBranchPublicationApprovalResponse>> RecordGitHubBranchPublicationApproval(
        [FromRoute] Guid runId,
        [FromBody] RecordGitHubBranchPublicationApprovalRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new RecordGitHubBranchPublicationApprovalCommand(
                runId, request.ApprovalId, request.OperationId, request.CommitSha, request.BranchName, request.TargetId,
                request.BindingId, request.RepositoryNodeId),
            cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new RecordGitHubBranchPublicationApprovalResponse(
            value.RunId,
            new GitHubBranchPublicationApprovalSourceResponse(value.Source.OperationId, value.Source.CommitSha, value.Source.BranchName),
            new GitHubBranchPublicationApprovalTargetResponse(
                value.Target.Id, value.Target.EventId, value.Target.Host, value.Target.Owner, value.Target.Repository,
                value.Target.RecordedAtUtc),
            new GitHubBranchPublicationApprovalBindingResponse(
                value.Binding.Id, value.Binding.EventId, value.Binding.RepositoryNodeId, value.Binding.Owner, value.Binding.Repository,
                value.Binding.ObservedAtUtc, value.Binding.RecordedAtUtc),
            new GitHubBranchPublicationApprovalResponse(
                value.Approval.Id, value.Approval.EventId, value.Approval.Host, value.Approval.Action, value.Approval.PolicyVersion,
                value.Approval.LocalCommitOperationId, value.Approval.WorkspaceId, value.Approval.TargetId, value.Approval.BindingId,
                value.Approval.RepositoryNodeId, value.Approval.Owner, value.Approval.Repository, value.Approval.BranchName,
                value.Approval.CommitSha, value.Approval.ApprovedAtUtc)));
    }
}
