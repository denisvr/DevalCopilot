using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchPublicationApproval;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetGitHubBranchPublicationApproval;

/// <summary>
/// Protected, bodyless read of the recorded GitHub branch publication approval of a run (ADR-0048). It reads persisted evidence only: no
/// lock, write, process or network. A query key, a declared body, any transfer encoding or a body byte is refused before dispatch. An
/// unknown run is the fixed 404. Available means recorded consent only, never readiness, a permission or a push.
/// </summary>
public sealed class GetGitHubBranchPublicationApprovalEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/github-branch-publication-approval")]
    [TypeFilter(typeof(PublicationApprovalReadGuardFilter))]
    [ProducesResponseType<GetGitHubBranchPublicationApprovalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetGitHubBranchPublicationApprovalResponse>> GetGitHubBranchPublicationApproval(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetGitHubBranchPublicationApprovalQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetGitHubBranchPublicationApprovalResponse(
            value.RunId,
            value.Status.ToString(),
            value.Source is null
                ? null
                : new GitHubBranchPublicationApprovalSourceResponse(value.Source.OperationId, value.Source.CommitSha, value.Source.BranchName),
            value.Target is null
                ? null
                : new GitHubBranchPublicationApprovalTargetResponse(
                    value.Target.Id, value.Target.EventId, value.Target.Host, value.Target.Owner, value.Target.Repository,
                    value.Target.RecordedAtUtc),
            value.Binding is null
                ? null
                : new GitHubBranchPublicationApprovalBindingResponse(
                    value.Binding.Id, value.Binding.EventId, value.Binding.RepositoryNodeId, value.Binding.Owner,
                    value.Binding.Repository, value.Binding.ObservedAtUtc, value.Binding.RecordedAtUtc),
            value.Approval is null
                ? null
                : new GitHubBranchPublicationApprovalResponse(
                    value.Approval.Id, value.Approval.EventId, value.Approval.Host, value.Approval.Action,
                    value.Approval.PolicyVersion, value.Approval.LocalCommitOperationId, value.Approval.WorkspaceId,
                    value.Approval.TargetId, value.Approval.BindingId, value.Approval.RepositoryNodeId, value.Approval.Owner,
                    value.Approval.Repository, value.Approval.BranchName, value.Approval.CommitSha, value.Approval.ApprovedAtUtc)));
    }
}
