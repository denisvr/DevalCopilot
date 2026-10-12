using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetGitHubStoredAuthentication;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.EnvironmentReadiness.GetGitHubStoredAuthentication;

public sealed class GetGitHubStoredAuthenticationEndpoint(IApplicationMediator mediator) : EnvironmentBaseEndpoint
{
    [HttpGet("github-stored-authentication")]
    public async Task<ActionResult<GitHubStoredAuthenticationResponse>> GetGitHubStoredAuthentication(CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetGitHubStoredAuthenticationQuery(), cancellationToken);

        return Ok(new GitHubStoredAuthenticationResponse(result.Host, result.Context, result.Status.ToString(), result.ObservedAtUtc));
    }
}
