using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.AspNetCore.Mvc;
using ApplicationCommand = DevalCopilot.Application.Features.EnvironmentReadiness.Commands.RequestHostCapabilityRefresh.RequestHostCapabilityRefreshCommand;

namespace DevalCopilot.Api.Features.EnvironmentReadiness.RequestHostCapabilityRefresh;

public sealed class RequestHostCapabilityRefreshEndpoint(
    IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails) : EnvironmentBaseEndpoint
{
    [HttpPost("capabilities/{capability}/refresh")]
    public async Task<ActionResult<RequestHostCapabilityRefreshResponse>> RequestHostCapabilityRefresh(
        [FromRoute] string capability, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Capability>(capability, ignoreCase: true, out var parsedCapability))
        {
            return NotFound();
        }

        var result = await mediator.SendAsync(new ApplicationCommand(parsedCapability), cancellationToken);

        return result.IsFailure
            ? problemDetails.CreateResponse(result, HttpContext)
            : Ok(new RequestHostCapabilityRefreshResponse(result.Value));
    }
}
