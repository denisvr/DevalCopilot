using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.Features.Runs.GetGitHubRepositoryIdentityBinding;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DevalCopilot.Api.Features.Runs.GetGitHubCheckContextsObservation;

/// <summary>
/// Protected, bodyless observation of the bounded check and status contexts GitHub returns for the exact recorded commit of a run whose
/// repository identity is bound (ADR-0045). It accepts no repository, node ID, branch, commit, cursor, limit, executable or credential and
/// refuses any request body before anything is observed. An unknown run is the fixed 404. It is not complete check or workflow evidence, a
/// required-check approval, a permission or publication authority.
/// </summary>
public sealed class GetGitHubCheckContextsObservationEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/github-check-contexts-observation")]
    [ProducesResponseType<GetGitHubCheckContextsObservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetGitHubCheckContextsObservationResponse>> GetGitHubCheckContextsObservation(
        [FromRoute] Guid runId, CancellationToken cancellationToken)
    {
        if (await HasBodyAsync(cancellationToken))
        {
            return problemDetails.CreateResponse(Result.Failure(GitHubCheckContextsObservationErrors.BodyNotAllowed()), HttpContext);
        }

        var result = await mediator.SendAsync(new GetGitHubCheckContextsObservationQuery(runId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetGitHubCheckContextsObservationResponse(
            value.RunId,
            value.Status.ToString(),
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
                    value.Binding.Repository, value.Binding.ObservedAtUtc, value.Binding.RecordedAtUtc),
            value.Observation is null
                ? null
                : new GitHubCheckContextsObservationResponse(
                    value.Observation.RepositoryNodeId,
                    value.Observation.Owner,
                    value.Observation.Repository,
                    value.Observation.ObservedAtUtc,
                    value.Observation.Ref is null
                        ? null
                        : new GitHubCheckContextsRefResponse(value.Observation.Ref.BranchName, value.Observation.Ref.CommitSha),
                    value.Observation.TotalCount,
                    value.Observation.Contexts?
                        .Select(context => new GitHubCheckContextResponse(
                            context.Kind, context.Id, context.Name, context.Status, context.Conclusion, context.CheckSuiteId))
                        .ToList())));
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
