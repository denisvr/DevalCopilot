using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryIdentityBinding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DevalCopilot.Api.Features.Runs.GetGitHubRepositoryIdentityBinding;

/// <summary>
/// Protected, bodyless read of the recorded GitHub repository identity binding of a run (ADR-0043). It reads persisted evidence only: no
/// lock, write, process or network, and it refuses any request body before dispatch. An unknown run is the fixed 404. A recorded binding is
/// historical evidence, never a fresh remote check, permission, CI success or publication authority.
/// </summary>
public sealed class GetGitHubRepositoryIdentityBindingEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/github-repository-identity-binding")]
    [ProducesResponseType<GetGitHubRepositoryIdentityBindingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetGitHubRepositoryIdentityBindingResponse>> GetGitHubRepositoryIdentityBinding(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        if (await HasBodyAsync(cancellationToken))
        {
            return problemDetails.CreateResponse(Result.Failure(GitHubRepositoryIdentityBindingErrors.BodyNotAllowed()), HttpContext);
        }

        var result = await mediator.SendAsync(new GetGitHubRepositoryIdentityBindingQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetGitHubRepositoryIdentityBindingResponse(
            value.RunId,
            value.State.ToString(),
            value.Source is null
                ? null
                : new GitHubObservationSourceResponse(value.Source.OperationId, value.Source.CommitSha, value.Source.BranchName),
            value.Target is null
                ? null
                : new GitHubObservationScopeResponse(
                    value.Target.Id, value.Target.EventId, value.Target.Host, value.Target.Owner, value.Target.Repository,
                    value.Target.RecordedAtUtc),
            value.Binding is null
                ? null
                : new GitHubRepositoryIdentityBindingResponse(
                    value.Binding.Id, value.Binding.EventId, value.Binding.RepositoryNodeId, value.Binding.Owner,
                    value.Binding.Repository, value.Binding.ObservedAtUtc, value.Binding.RecordedAtUtc)));
    }

    /// <summary>A declared length, any transfer encoding (including an empty chunked one) or any readable byte is a body; one byte is peeked
    /// so an undeclared body is seen too.</summary>
    private async Task<bool> HasBodyAsync(CancellationToken cancellationToken)
    {
        var request = HttpContext.Request;
        if (request.ContentLength is > 0 || request.Headers.ContainsKey(HeaderNames.TransferEncoding))
        {
            return true;
        }

        return await request.Body.ReadAsync(new byte[1], cancellationToken) > 0;
    }
}
