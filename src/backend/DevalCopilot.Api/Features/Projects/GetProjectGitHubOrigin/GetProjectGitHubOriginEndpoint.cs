using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Projects.GetProjectGitHubOrigin;

/// <summary>
/// Protected, bodyless, read-only observation of one registered project's repository-local GitHub origin (ADR-0039). An
/// unknown project is the fixed 404 through the shared Problem Details contract. It carries no client-chosen path, remote or
/// URL, observes configured scope only, and is never verified remote identity, permission, CI success or publication authority.
/// </summary>
public sealed class GetProjectGitHubOriginEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : ProjectsBaseEndpoint
{
    [HttpGet("{projectId:guid}/github-origin")]
    [ProducesResponseType<ProjectGitHubOriginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectGitHubOriginResponse>> GetProjectGitHubOrigin(
        [FromRoute] Guid projectId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetProjectGitHubOriginQuery(projectId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;

        return Ok(new ProjectGitHubOriginResponse(
            value.ProjectId,
            value.Source,
            value.Status.ToString(),
            value.ObservedAtUtc,
            value.Target is null ? null : new GitHubOriginTargetResponse(value.Target.Host, value.Target.Owner, value.Target.Repository)));
    }
}
