using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Api.Features.Runs.GetGitHubRepositoryIdentityBinding;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DevalCopilot.Api.Features.Runs.GetGitHubCheckRunOutputObservation;

/// <summary>
/// Protected, bodyless inspection of the title, summary and text of one GitHub check run associated with the exact recorded commit of a
/// run whose repository identity is bound (ADR-0047). The only client input is the one opaque <c>checkRunId</c> query value; a missing,
/// duplicate, extra, differently spelled or invalid query and any request body are refused before anything is dispatched or observed
/// with fixed, non-echoing 400 answers. An unknown run is the fixed 404. The output is untrusted, post-redaction provider text for one
/// moment: not complete check or workflow evidence, a diagnosis, a required-check approval, a permission or publication authority.
/// </summary>
public sealed class GetGitHubCheckRunOutputObservationEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    private const string CheckRunIdKey = "checkRunId";

    [HttpGet("{runId:guid}/github-check-run-output-observation")]
    [ProducesResponseType<GetGitHubCheckRunOutputObservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetGitHubCheckRunOutputObservationResponse>> GetGitHubCheckRunOutputObservation(
        [FromRoute] Guid runId, [FromQuery] string? checkRunId, CancellationToken cancellationToken)
    {
        if (await HasBodyAsync(cancellationToken))
        {
            return problemDetails.CreateResponse(Result.Failure(GitHubCheckRunOutputObservationErrors.BodyNotAllowed()), HttpContext);
        }

        if (!HasExactlyTheCheckRunIdKey() || !GitHubCheckRunOutputPolicy.IsCheckRunId(checkRunId))
        {
            return problemDetails.CreateResponse(Result.Failure(GitHubCheckRunOutputObservationErrors.InvalidRequest()), HttpContext);
        }

        var result = await mediator.SendAsync(new GetGitHubCheckRunOutputObservationQuery(runId, checkRunId!), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetGitHubCheckRunOutputObservationResponse(
            value.RunId,
            value.CheckRunId,
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
                : new GitHubCheckRunOutputObservationResponse(
                    value.Observation.RepositoryNodeId,
                    value.Observation.Owner,
                    value.Observation.Repository,
                    value.Observation.ObservedAtUtc,
                    value.Observation.Ref is null
                        ? null
                        : new GitHubCheckRunOutputRefResponse(value.Observation.Ref.BranchName, value.Observation.Ref.CommitSha),
                    value.Observation.CheckRun is null
                        ? null
                        : new GitHubCheckRunOutputCheckRunResponse(
                            value.Observation.CheckRun.Id,
                            value.Observation.CheckRun.CheckSuiteId,
                            value.Observation.CheckRun.CommitSha,
                            value.Observation.CheckRun.Name,
                            value.Observation.CheckRun.Status,
                            value.Observation.CheckRun.Conclusion,
                            value.Observation.CheckRun.Output is null
                                ? null
                                : new GitHubCheckRunOutputTextResponse(
                                    value.Observation.CheckRun.Output.Title,
                                    value.Observation.CheckRun.Output.Summary,
                                    value.Observation.CheckRun.Output.Text)))));
    }

    /// <summary>Exactly one query key, spelled exactly <c>checkRunId</c>, carrying exactly one value. A repeated key (in any letter case)
    /// is one key with several values, and a differently spelled or extra key is a second key, so all are refused. The value itself is
    /// held to the identifier contract by the caller: nothing is trimmed or normalized and the rejected input is never echoed.</summary>
    private bool HasExactlyTheCheckRunIdKey()
    {
        var query = HttpContext.Request.Query;
        if (query.Count != 1)
        {
            return false;
        }

        var pair = query.Single();

        return string.Equals(pair.Key, CheckRunIdKey, StringComparison.Ordinal) && pair.Value.Count == 1;
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
