using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetGitHubRepositoryIdentityBinding;
using DevalCopilot.Application.Features.Runs.Commands.BindGitHubRepositoryIdentity;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.BindGitHubRepositoryIdentity;

/// <summary>
/// Explicitly binds the opaque GitHub repository node identifier the Human confirmed to a run's recorded ADR-0040 target, but only after a
/// fresh host observation through the existing identity observer agrees (ADR-0043). The body holds exactly four members, is bounded to
/// 8 KiB, and unknown or duplicate members are refused before anything is dispatched. The host resolves the pinned scope, observes the
/// identity once with nothing locked, then saves the binding and one Human-authored event atomically. A replay of the same coherent
/// binding returns the original record without another observation. The binding is historical identity evidence, never permission, branch
/// or commit proof, CI approval or publication authority.
/// </summary>
public sealed class BindGitHubRepositoryIdentityEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const int MaximumRequestBodyBytes = 8 * 1024;

    [HttpPost("{runId:guid}/github-repository-identity-binding")]
    [RequestSizeLimit(MaximumRequestBodyBytes)]
    [TypeFilter(typeof(StrictBindingBodyFilter))]
    [ProducesResponseType<BindGitHubRepositoryIdentityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BindGitHubRepositoryIdentityResponse>> BindGitHubRepositoryIdentity(
        [FromRoute] Guid runId, [FromBody] BindGitHubRepositoryIdentityRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(
            new BindGitHubRepositoryIdentityCommand(
                runId, request.OperationId, request.CommitSha, request.TargetId, request.RepositoryNodeId),
            cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new BindGitHubRepositoryIdentityResponse(
            value.RunId,
            new GitHubObservationSourceResponse(value.Source.OperationId, value.Source.CommitSha, value.Source.BranchName),
            new GitHubObservationScopeResponse(
                value.Target.Id, value.Target.EventId, value.Target.Host, value.Target.Owner, value.Target.Repository,
                value.Target.RecordedAtUtc),
            new GitHubRepositoryIdentityBindingResponse(
                value.Binding.Id, value.Binding.EventId, value.Binding.RepositoryNodeId, value.Binding.Owner, value.Binding.Repository,
                value.Binding.ObservedAtUtc, value.Binding.RecordedAtUtc)));
    }
}
