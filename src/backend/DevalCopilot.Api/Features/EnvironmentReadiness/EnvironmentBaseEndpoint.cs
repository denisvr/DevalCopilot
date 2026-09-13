using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.EnvironmentReadiness;

[ApiController]
[Authorize]
[Route("api/environment")]
public abstract class EnvironmentBaseEndpoint : ControllerBase;
