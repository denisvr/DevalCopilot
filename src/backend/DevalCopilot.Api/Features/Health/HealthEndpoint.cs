using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Health;

/// <summary>
/// Operational liveness probe. It has no application command or query semantics, so it
/// bypasses <c>IApplicationMediator</c> per the standing exception for liveness and
/// readiness endpoints, and is the one endpoint in this host that explicitly opts out
/// of the authenticated default-deny policy.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("health")]
public sealed class HealthEndpoint : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}
